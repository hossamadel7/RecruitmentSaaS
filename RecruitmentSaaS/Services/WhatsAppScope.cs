using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Services
{
    /// <summary>Whose chats a user may see. <see cref="Agents"/> null = every chat.</summary>
    public sealed class WhatsAppVisibility
    {
        public WhatsAppVisibility(List<Guid>? agents, Guid? teamLeaderId)
        {
            Agents = agents;
            TeamLeaderId = teamLeaderId;
        }

        /// <summary>Salespeople whose chats are visible; null for admin / team leaders (everything).</summary>
        public List<Guid>? Agents { get; }

        /// <summary>For the TeleSales manager: their team, whose unassigned (waiting) chats they also see.</summary>
        public Guid? TeamLeaderId { get; }

        public bool SeesAll => Agents == null;
    }

    /// <summary>
    /// Whose WhatsApp chats a user may see. Admin and team leaders see everything;
    /// the TeleSales manager (role 8) sees their own chats, their team's, and their team's chats
    /// waiting for assignment (after a member was deactivated); everyone else sees only their own.
    /// </summary>
    public static class WhatsAppScope
    {
        public static async Task<WhatsAppVisibility> ForUserAsync(RecruitmentCrmContext db, string? role, Guid userId, CancellationToken ct = default)
        {
            if (WhatsAppAuthorization.IsOrgWide(role)) return new WhatsAppVisibility(null, null);
            if (role != "8") return new WhatsAppVisibility(new List<Guid> { userId }, null);

            var teamLeaderId = await db.Users.AsNoTracking()
                .Where(u => u.Id == userId).Select(u => u.ManagerId).FirstOrDefaultAsync(ct);
            var ids = teamLeaderId == null
                ? new List<Guid>()
                : await db.Users.AsNoTracking()
                    .Where(u => u.ManagerId == teamLeaderId && (u.Role == 3 || u.Role == 8))
                    .Select(u => u.Id).ToListAsync(ct);
            if (!ids.Contains(userId)) ids.Add(userId);
            return new WhatsAppVisibility(ids, teamLeaderId);
        }

        /// <summary>The salespeople whose chats are visible (null = everyone).</summary>
        public static async Task<List<Guid>?> VisibleAgentIdsAsync(RecruitmentCrmContext db, string? role, Guid userId, CancellationToken ct = default) =>
            (await ForUserAsync(db, role, userId, ct)).Agents;

        public static IQueryable<WhatsAppConversation> ApplyVisibility(this IQueryable<WhatsAppConversation> query, WhatsAppVisibility v)
        {
            if (v.SeesAll) return query;
            var agents = v.Agents!;
            var leader = v.TeamLeaderId;
            return leader == null
                ? query.Where(c => c.AssignedSalesAgentId != null && agents.Contains(c.AssignedSalesAgentId.Value))
                : query.Where(c => (c.AssignedSalesAgentId != null && agents.Contains(c.AssignedSalesAgentId.Value))
                                || (c.AssignedSalesAgentId == null && c.PendingTeamManagerId == leader));
        }

        public static bool CanSee(WhatsAppVisibility v, Guid? assignedAgentId, Guid? pendingTeamManagerId) =>
            v.SeesAll
            || (assignedAgentId.HasValue
                ? v.Agents!.Contains(assignedAgentId.Value)
                : v.TeamLeaderId != null && pendingTeamManagerId == v.TeamLeaderId);

        /// <summary>For agent-only checks (who a chat may be given to).</summary>
        public static bool CanSee(List<Guid>? visibleAgents, Guid? agentId) =>
            visibleAgents == null || (agentId.HasValue && visibleAgents.Contains(agentId.Value));
    }
}
