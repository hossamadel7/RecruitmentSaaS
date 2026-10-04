using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Services
{
    /// <summary>
    /// Round-robin lead assignment to TeleSales (there is no shared "pool" anymore).
    /// The next person is the active TeleSales whose most recent lead is the oldest (or who has none),
    /// so new leads spread evenly no matter where earlier ones came from.
    /// </summary>
    public static class LeadDistributor
    {
        public static async Task<Guid?> NextTeleSalesAsync(RecruitmentCrmContext db, IQueryable<User> candidates, CancellationToken ct = default)
        {
            var ids = await candidates
                .Where(u => u.Role == 3 && u.IsActive)
                .Select(u => u.Id)
                .ToListAsync(ct);
            if (ids.Count == 0) return null;

            // When did each person last *receive* a lead? A new lead counts at its creation; an older
            // lead handed over later counts at its "assigned" activity (type 8) — otherwise a backlog of
            // old leads would all go to the same person.
            var lastCreated = await db.Leads
                .Where(l => l.AssignedSalesId != null && ids.Contains(l.AssignedSalesId.Value))
                .GroupBy(l => l.AssignedSalesId!.Value)
                .Select(g => new { UserId = g.Key, Last = g.Max(l => l.CreatedAt) })
                .ToDictionaryAsync(x => x.UserId, x => x.Last, ct);

            var lastAssigned = await db.LeadActivities
                .Where(a => a.ActivityType == 8 && a.Lead.AssignedSalesId != null && ids.Contains(a.Lead.AssignedSalesId.Value))
                .GroupBy(a => a.Lead.AssignedSalesId!.Value)
                .Select(g => new { UserId = g.Key, Last = g.Max(a => a.CreatedAt) })
                .ToDictionaryAsync(x => x.UserId, x => x.Last, ct);

            DateTime LastReceived(Guid id)
            {
                var a = lastCreated.TryGetValue(id, out var c) ? c : DateTime.MinValue;
                var b = lastAssigned.TryGetValue(id, out var s) ? s : DateTime.MinValue;
                return a > b ? a : b;
            }

            return ids.OrderBy(LastReceived).ThenBy(id => id).First();
        }

        /// <summary>Everyone who may receive a Google Sheets lead: the sheet's assigned users, else all TeleSales.</summary>
        public static async Task<IQueryable<User>> CandidatesForSheetAsync(RecruitmentCrmContext db, Guid? sheetId, CancellationToken ct = default)
        {
            if (sheetId == null) return db.Users;

            var sheetUsers = db.Users.Where(u => db.SalesGoogleSheetUsers.Any(su =>
                su.SheetId == sheetId && su.SalesUserId == u.Id && su.IsActive));
            return await sheetUsers.AnyAsync(u => u.Role == 3 && u.IsActive, ct) ? sheetUsers : db.Users;
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
                var candidates = await LeadDistributor.CandidatesForSheetAsync(db, lead.GoogleSheetId, ct);
                var salesId = await LeadDistributor.NextTeleSalesAsync(db, candidates, ct);
                if (salesId == null)
                {
                    _logger.LogWarning("No active TeleSales to receive lead {LeadId}", lead.Id);
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
