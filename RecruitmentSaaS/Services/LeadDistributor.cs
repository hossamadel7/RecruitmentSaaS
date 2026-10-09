using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Services
{
    /// <summary>
    /// Weighted round-robin lead assignment to TeleSales (there is no shared "pool" anymore).
    /// Each rotation is an ordered list of people, each taking <c>Share</c> leads per turn
    /// (e.g. 3, then 2, then 1, 1, 1 — set by the admin or the team manager; 0 = paused).
    /// Rotations: "all" for general leads, "team:{managerId}" for a team form's leads.
    /// </summary>
    public static class LeadDistributor
    {
        public const string AllScope = "all";

        /// <summary>
        /// How general leads are split between teams (rows = team leaders, Share = leads per turn).
        /// Each team's share then goes through that team's own rotation. No team with a share = the
        /// per-person general rotation ("all") as before.
        /// </summary>
        public const string TeamsScope = "teams";

        public static string TeamScope(Guid managerId) => "team:" + managerId.ToString("N");

        public static Guid? ManagerOfScope(string? scope) =>
            scope != null && scope.StartsWith("team:") && Guid.TryParse(scope[5..], out var id) ? id : null;

        public record RotationSlot(Guid UserId, string Name, short Share);

        // One pick at a time across the app, so two leads arriving together never get the same turn
        private static readonly SemaphoreSlim Gate = new(1, 1);

        /// <summary>The active TeleSales among <paramref name="candidates"/>, in rotation order with their shares.</summary>
        public static async Task<List<RotationSlot>> RotationAsync(RecruitmentCrmContext db, IQueryable<User> candidates, string configScope, CancellationToken ct = default)
        {
            var users = await candidates
                .Where(u => (u.Role == 3 || u.Role == 8) && u.IsActive)   // 8 = head TeleSales, also takes leads
                .Select(u => new { u.Id, Name = u.FullNameAr != null && u.FullNameAr != "" ? u.FullNameAr : u.FullName })
                .ToListAsync(ct);
            var ids = users.Select(u => u.Id).ToList();

            var config = await db.LeadRotationMembers.AsNoTracking()
                .Where(m => m.ScopeKey == configScope && ids.Contains(m.UserId))
                .ToDictionaryAsync(m => m.UserId, ct);

            // People not configured yet (e.g. just joined) come last with 1 lead per turn
            return users
                .OrderBy(u => config.TryGetValue(u.Id, out var m) ? m.Position : int.MaxValue)
                .ThenBy(u => u.Name).ThenBy(u => u.Id)
                .Select(u => new RotationSlot(u.Id, u.Name, config.TryGetValue(u.Id, out var m) ? m.Share : (short)1))
                .ToList();
        }

        /// <summary>Teams that can take general leads (active leader with at least one active TeleSales), in order with their shares.</summary>
        public static async Task<List<RotationSlot>> TeamRotationAsync(RecruitmentCrmContext db, CancellationToken ct = default)
        {
            var teams = await db.Users.AsNoTracking()
                .Where(u => u.Role == 7 && u.IsActive
                         && db.Users.Any(m => m.ManagerId == u.Id && (m.Role == 3 || m.Role == 8) && m.IsActive))
                .Select(u => new { u.Id, Name = u.FullNameAr != null && u.FullNameAr != "" ? u.FullNameAr : u.FullName })
                .ToListAsync(ct);
            var ids = teams.Select(t => t.Id).ToList();
            var config = await db.LeadRotationMembers.AsNoTracking()
                .Where(m => m.ScopeKey == TeamsScope && ids.Contains(m.UserId))
                .ToDictionaryAsync(m => m.UserId, ct);
            // A team not set yet gets 0: the split only starts when the admin sets it
            return teams
                .OrderBy(t => config.TryGetValue(t.Id, out var m) ? m.Position : int.MaxValue)
                .ThenBy(t => t.Name).ThenBy(t => t.Id)
                .Select(t => new RotationSlot(t.Id, t.Name, config.TryGetValue(t.Id, out var m) ? m.Share : (short)0))
                .ToList();
        }

        /// <summary>
        /// A lead that belongs to no team (main site form, a WhatsApp number with no team, Facebook / Sheets):
        /// split between the teams by their shares, then the team's own rotation picks the TeleSales.
        /// Falls back to the per-person general rotation when no team has a share (or the team has nobody free).
        /// Also returns the team it went to.
        /// </summary>
        public static async Task<(Guid? SalesId, Guid? Team)> NextForGeneralLeadAsync(RecruitmentCrmContext db, CancellationToken ct = default)
        {
            var teams = await TeamRotationAsync(db, ct);
            if (teams.Any(t => t.Share > 0))
            {
                Guid team;
                await Gate.WaitAsync(ct);
                try
                {
                    var state = await db.LeadRotationStates.FirstOrDefaultAsync(s => s.ScopeKey == TeamsScope, ct);
                    if (state == null)
                    {
                        state = new LeadRotationState { ScopeKey = TeamsScope };
                        db.LeadRotationStates.Add(state);
                    }
                    var (teamId, given) = Advance(teams, state.CurrentUserId, state.GivenInTurn);
                    state.CurrentUserId = teamId;
                    state.GivenInTurn = given;
                    state.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);
                    team = teamId;
                }
                finally
                {
                    Gate.Release();
                }
                var inTeam = await NextTeleSalesAsync(db, db.Users.Where(u => u.ManagerId == team), TeamScope(team), ct);
                if (inTeam != null) return (inTeam, team);
            }
            return (await NextTeleSalesAsync(db, db.Users, AllScope, ct), null);
        }

        public static Task<Guid?> NextTeleSalesAsync(RecruitmentCrmContext db, IQueryable<User> candidates, string scope, CancellationToken ct = default) =>
            NextTeleSalesAsync(db, candidates, scope, scope, ct);

        /// <summary>
        /// Picks who gets the next lead and moves the rotation on. <paramref name="stateScope"/> lets a
        /// subset (a Google Sheet's users) keep its own turn while using the <paramref name="configScope"/> shares.
        /// Returns null when nobody can receive leads (no active TeleSales, or all paused).
        /// </summary>
        public static async Task<Guid?> NextTeleSalesAsync(RecruitmentCrmContext db, IQueryable<User> candidates,
            string configScope, string stateScope, CancellationToken ct = default)
        {
            var rotation = await RotationAsync(db, candidates, configScope, ct);
            if (!rotation.Any(r => r.Share > 0)) return null;

            await Gate.WaitAsync(ct);
            try
            {
                var state = await db.LeadRotationStates.FirstOrDefaultAsync(s => s.ScopeKey == stateScope, ct);
                if (state == null)
                {
                    state = new LeadRotationState { ScopeKey = stateScope };
                    db.LeadRotationStates.Add(state);
                }

                var (userId, given) = Advance(rotation, state.CurrentUserId, state.GivenInTurn);
                state.CurrentUserId = userId;
                state.GivenInTurn = given;
                state.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                return userId;
            }
            finally
            {
                Gate.Release();
            }
        }

        /// <summary>
        /// One step of the rotation: the current person keeps the turn until they've had their share,
        /// then it passes to the next person (in order) whose share isn't 0. If the current person
        /// left the rotation, it starts again from the top.
        /// </summary>
        public static (Guid UserId, int GivenInTurn) Advance(IReadOnlyList<RotationSlot> rotation, Guid? currentUserId, int givenInTurn)
        {
            var index = -1;
            for (var i = 0; i < rotation.Count; i++)
                if (rotation[i].UserId == currentUserId) { index = i; break; }

            if (index >= 0 && givenInTurn < rotation[index].Share)
                return (rotation[index].UserId, givenInTurn + 1);

            for (var step = 1; step <= rotation.Count; step++)
            {
                var next = rotation[(index + step) % rotation.Count];
                if (next.Share > 0) return (next.UserId, 1);
            }
            throw new InvalidOperationException("Rotation has nobody with a share above 0");
        }

        /// <summary>Who the next <paramref name="count"/> leads would go to, without changing anything.</summary>
        public static List<Guid> Preview(IReadOnlyList<RotationSlot> rotation, Guid? currentUserId, int givenInTurn, int count)
        {
            var result = new List<Guid>();
            if (!rotation.Any(r => r.Share > 0)) return result;
            for (var i = 0; i < count; i++)
            {
                var (userId, given) = Advance(rotation, currentUserId, givenInTurn);
                result.Add(userId);
                currentUserId = userId;
                givenInTurn = given;
            }
            return result;
        }

        /// <summary>
        /// Who may receive a Google Sheets lead: the sheet's assigned users (their own turn, the general
        /// shares), else every TeleSales in the general rotation.
        /// </summary>
        public static async Task<(IQueryable<User> Candidates, string StateScope)> CandidatesForSheetAsync(RecruitmentCrmContext db, Guid? sheetId, CancellationToken ct = default)
        {
            if (sheetId == null) return (db.Users, AllScope);

            var sheetUsers = db.Users.Where(u => db.SalesGoogleSheetUsers.Any(su =>
                su.SheetId == sheetId && su.SalesUserId == u.Id && su.IsActive));
            return await sheetUsers.AnyAsync(u => (u.Role == 3 || u.Role == 8) && u.IsActive, ct)
                ? (sheetUsers, "sheet:" + sheetId.Value.ToString("N"))
                : (db.Users, AllScope);
        }
    }

    /// <summary>
    /// Every minute, hands any unassigned lead (Facebook ads, Google Sheets, anything else that
    /// arrived without a salesperson) to the next TeleSales and notifies them. Team-form leads
    /// (they belong to a manager) and reception walk-ins are left alone.
    /// </summary>
    public class LeadAutoAssignService : BackgroundService
    {
        private const int BatchSize = 200;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<LeadAutoAssignService> _logger;

        public LeadAutoAssignService(IServiceScopeFactory scopeFactory, ILogger<LeadAutoAssignService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); // let the app finish starting
            while (!stoppingToken.IsCancellationRequested)
            {
                try { await AssignWaitingLeadsAsync(stoppingToken); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Automatic lead assignment failed");
                }
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        private async Task AssignWaitingLeadsAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentCrmContext>();
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

            var waiting = await db.Leads
                .Where(l => l.AssignedSalesId == null
                         && l.TeamManagerId == null       // team forms: the manager decides
                         && l.LeadSource != 3             // reception walk-ins go to office sales
                         && !l.IsConverted
                         && !l.IsDuplicate)
                .OrderBy(l => l.CreatedAt)
                .Take(BatchSize)
                .ToListAsync(ct);
            if (waiting.Count == 0) return;

            var assigned = 0;
            foreach (var lead in waiting)
            {
                var (candidates, stateScope) = await LeadDistributor.CandidatesForSheetAsync(db, lead.GoogleSheetId, ct);
                Guid? salesId;
                if (stateScope == LeadDistributor.AllScope)
                {
                    // No sheet team: split between the teams like every general lead
                    var (generalId, generalTeam) = await LeadDistributor.NextForGeneralLeadAsync(db, ct);
                    salesId = generalId;
                    if (generalId != null && generalTeam != null) lead.TeamManagerId ??= generalTeam;
                }
                else
                    salesId = await LeadDistributor.NextTeleSalesAsync(db, candidates, LeadDistributor.AllScope, stateScope, ct);
                if (salesId == null)
                {
                    _logger.LogWarning("No active TeleSales (or all paused) to receive lead {LeadId}", lead.Id);
                    break;
                }

                lead.AssignedSalesId = salesId;
                lead.UpdatedAt = DateTime.UtcNow;
                db.LeadActivities.Add(new LeadActivity
                {
                    Id = Guid.NewGuid(),
                    LeadId = lead.Id,
                    ActivityType = 8,
                    Description = "تم تعيين العميل تلقائياً (توزيع بالدور)",
                    ActorType = 2, // system (DB allows 1 = user, 2 = system)
                    CreatedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync(ct); // one at a time so the next pick sees this assignment
                assigned++;

                try
                {
                    await notifications.SendAsync(salesId.Value, "عميل جديد تم تعيينه لك",
                        $"{lead.FullName} — {lead.Phone}", link: $"/TeleSales/LeadDetail/{lead.Id}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Lead {LeadId} assigned but the notification failed", lead.Id);
                }
            }

            if (assigned > 0)
                _logger.LogInformation("Auto-assigned {Count} waiting lead(s) to TeleSales", assigned);
        }
    }
}
