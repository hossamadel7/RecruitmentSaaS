using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Services
{
    /// <summary>Whose chats a user may see.</summary>
    public sealed class WhatsAppVisibility
    {
        public WhatsAppVisibility(List<Guid>? agents, Guid? teamLeaderId, bool allWaiting = false, List<Guid>? teamAccountIds = null)
        {
            Agents = agents;
            TeamLeaderId = teamLeaderId;
            AllWaiting = allWaiting;
            TeamAccountIds = teamAccountIds ?? new List<Guid>();
        }

        /// <summary>The team's own WhatsApp numbers: their chats nobody has taken yet are visible too.</summary>
        public List<Guid> TeamAccountIds { get; }

        /// <summary>Salespeople whose assigned chats are visible; null = every salesperson's (admin).</summary>
        public List<Guid>? Agents { get; }

        /// <summary>The team whose waiting chats (left by a deactivated member) are visible.</summary>
        public Guid? TeamLeaderId { get; }

        /// <summary>Every team's waiting chats are visible (admin only).</summary>
        public bool AllWaiting { get; }

        public bool SeesAll => Agents == null && AllWaiting;
    }

    /// <summary>
    /// Whose WhatsApp chats a user may see. Admin sees everything. A team leader (role 7) and the
    /// TeleSales manager (role 8) see only their own team: chats assigned to its members, its waiting
    /// chats (left by a deactivated member) and the not-yet-taken chats on the team's WhatsApp number.
    /// Everyone else sees only their own.
    /// </summary>
    public static class WhatsAppScope
    {
        public static async Task<WhatsAppVisibility> ForUserAsync(RecruitmentCrmContext db, string? role, Guid userId, CancellationToken ct = default)
        {
            if (WhatsAppAuthorization.IsAdmin(role)) return new WhatsAppVisibility(null, null, allWaiting: true);
            if (role != "7" && role != "8") return new WhatsAppVisibility(new List<Guid> { userId }, null);

            // The team: a team leader's own, or the TeleSales manager's team leader's
            var teamLeaderId = role == "7"
                ? userId
                : await db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.ManagerId).FirstOrDefaultAsync(ct);
            var ids = teamLeaderId == null
                ? new List<Guid>()
                : await db.Users.AsNoTracking()
                    .Where(u => u.ManagerId == teamLeaderId && (u.Role == 3 || u.Role == 8))
                    .Select(u => u.Id).ToListAsync(ct);
            if (!ids.Contains(userId)) ids.Add(userId);
            var accounts = teamLeaderId == null ? new List<Guid>() : await TeamAccountIdsAsync(db, teamLeaderId.Value, ct);
            return new WhatsAppVisibility(ids, teamLeaderId, teamAccountIds: accounts);
        }

        /// <summary>The WhatsApp numbers that belong to a team (the number on its registration form).</summary>
        public static async Task<List<Guid>> TeamAccountIdsAsync(RecruitmentCrmContext db, Guid teamLeaderId, CancellationToken ct = default)
        {
            var numbers = (await db.TeamLeadForms.AsNoTracking()
                    .Where(f => f.ManagerId == teamLeaderId && f.WhatsAppNumber != null)
                    .Select(f => f.WhatsAppNumber!).ToListAsync(ct))
                .Select(Digits).Where(d => d.Length >= 8).ToHashSet();
            if (numbers.Count == 0) return new List<Guid>();
            return (await db.WhatsAppAccounts.AsNoTracking().Select(a => new { a.Id, a.DisplayPhoneNumber }).ToListAsync(ct))
                .Where(a => numbers.Contains(Digits(a.DisplayPhoneNumber))).Select(a => a.Id).ToList();
        }

        /// <summary>The team whose number a chat came in on (null = a general number).</summary>
        public static async Task<Guid?> TeamOfAccountAsync(RecruitmentCrmContext db, Guid accountId, CancellationToken ct = default)
        {
            var number = await db.WhatsAppAccounts.AsNoTracking().Where(a => a.Id == accountId).Select(a => a.DisplayPhoneNumber).FirstOrDefaultAsync(ct);
            if (number == null) return null;
            var digits = Digits(number);
            var forms = await db.TeamLeadForms.AsNoTracking()
                .Where(f => f.WhatsAppNumber != null && f.Manager.IsActive)
                .Select(f => new { f.ManagerId, f.WhatsAppNumber }).ToListAsync(ct);
            return forms.FirstOrDefault(f => Digits(f.WhatsAppNumber!) == digits)?.ManagerId;
        }

        private static string Digits(string? s) => new string((s ?? "").Where(char.IsDigit).ToArray());

        /// <summary>The salespeople whose chats are visible (null = everyone).</summary>
        public static async Task<List<Guid>?> VisibleAgentIdsAsync(RecruitmentCrmContext db, string? role, Guid userId, CancellationToken ct = default) =>
            (await ForUserAsync(db, role, userId, ct)).Agents;

        public static IQueryable<WhatsAppConversation> ApplyVisibility(this IQueryable<WhatsAppConversation> query, WhatsAppVisibility v)
        {
            if (v.SeesAll || v.Agents == null) return query;
            var leader = v.TeamLeaderId;
            var agents = v.Agents;
            var accounts = v.TeamAccountIds;
            return leader == null
                ? query.Where(c => c.AssignedSalesAgentId != null && agents.Contains(c.AssignedSalesAgentId.Value))
                : query.Where(c => (c.AssignedSalesAgentId != null && agents.Contains(c.AssignedSalesAgentId.Value))
                                || (c.AssignedSalesAgentId == null && c.PendingTeamManagerId == leader)
                                || (c.AssignedSalesAgentId == null && c.PendingTeamManagerId == null && accounts.Contains(c.WhatsAppAccountId)));
        }

        public static bool CanSee(WhatsAppVisibility v, Guid? assignedAgentId, Guid? pendingTeamManagerId, Guid accountId)
        {
            if (v.SeesAll || v.Agents == null) return true;
            if (assignedAgentId.HasValue) return v.Agents.Contains(assignedAgentId.Value);
            if (pendingTeamManagerId != null) return pendingTeamManagerId == v.TeamLeaderId;   // a team's waiting chat: that team only
            return v.TeamLeaderId != null && v.TeamAccountIds.Contains(accountId);           // not taken yet: the number's team
        }

        /// <summary>For agent-only checks (who a chat may be given to).</summary>
        public static bool CanSee(List<Guid>? visibleAgents, Guid? agentId) =>
            visibleAgents == null || (agentId.HasValue && visibleAgents.Contains(agentId.Value));
    }
}
