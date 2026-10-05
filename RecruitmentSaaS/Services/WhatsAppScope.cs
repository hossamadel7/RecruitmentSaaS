using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Services
{
    /// <summary>Whose chats a user may see.</summary>
    public sealed class WhatsAppVisibility
    {
        public WhatsAppVisibility(List<Guid>? agents, Guid? teamLeaderId, bool allWaiting = false)
        {
            Agents = agents;
            TeamLeaderId = teamLeaderId;
            AllWaiting = allWaiting;
        }

        /// <summary>Salespeople whose assigned chats are visible; null = every salesperson's (admin, team leaders).</summary>
        public List<Guid>? Agents { get; }

        /// <summary>The team whose waiting chats (left by a deactivated member) are visible.</summary>
        public Guid? TeamLeaderId { get; }

        /// <summary>Every team's waiting chats are visible (admin only).</summary>
        public bool AllWaiting { get; }

        public bool SeesAll => Agents == null && AllWaiting;
    }

    /// <summary>
    /// Whose WhatsApp chats a user may see. Admin sees everything. Team leaders see every assigned
    /// chat but only their own team's waiting chats (left by a deactivated member). The TeleSales
    /// manager (role 8) sees their own chats, their team's, and their team's waiting chats.
    /// Everyone else sees only their own.
    /// </summary>
    public static class WhatsAppScope
    {
        public static async Task<WhatsAppVisibility> ForUserAsync(RecruitmentCrmContext db, string? role, Guid userId, CancellationToken ct = default)
        {
            if (WhatsAppAuthorization.IsAdmin(role)) return new WhatsAppVisibility(null, null, allWaiting: true);
            if (role == "7") return new WhatsAppVisibility(null, userId);   // team leader: their team = themselves
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
            var leader = v.TeamLeaderId;
            if (v.Agents == null)   // team leader: everything except other teams' waiting chats
                return query.Where(c => c.AssignedSalesAgentId != null || c.PendingTeamManagerId == null || c.PendingTeamManagerId == leader);

            var agents = v.Agents;
            return leader == null
                ? query.Where(c => c.AssignedSalesAgentId != null && agents.Contains(c.AssignedSalesAgentId.Value))
                : query.Where(c => (c.AssignedSalesAgentId != null && agents.Contains(c.AssignedSalesAgentId.Value))
                                || (c.AssignedSalesAgentId == null && c.PendingTeamManagerId == leader));
        }

        public static bool CanSee(WhatsAppVisibility v, Guid? assignedAgentId, Guid? pendingTeamManagerId)
        {
            if (v.SeesAll) return true;
            if (assignedAgentId.HasValue) return v.Agents == null || v.Agents.Contains(assignedAgentId.Value);
            if (pendingTeamManagerId == null) return v.Agents == null;   // unowned chat with no team: team leaders
            return pendingTeamManagerId == v.TeamLeaderId;               // a team's waiting chat: that team only
        }

        /// <summary>For agent-only checks (who a chat may be given to).</summary>
        public static bool CanSee(List<Guid>? visibleAgents, Guid? agentId) =>
            visibleAgents == null || (agentId.HasValue && visibleAgents.Contains(agentId.Value));
    }
}
