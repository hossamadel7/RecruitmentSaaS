using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;

namespace RecruitmentSaaS.Services
{
    /// <summary>
    /// Whose WhatsApp chats a user may see. Admin and team leaders see everything (null);
    /// the TeleSales manager (role 8) sees their own chats plus their team's (same team leader);
    /// everyone else sees only their own.
    /// </summary>
    public static class WhatsAppScope
    {
        public static async Task<List<Guid>?> VisibleAgentIdsAsync(RecruitmentCrmContext db, string? role, Guid userId, CancellationToken ct = default)
        {
            if (WhatsAppAuthorization.IsOrgWide(role)) return null;
            if (role != "8") return new List<Guid> { userId };

            var teamLeaderId = await db.Users.AsNoTracking()
                .Where(u => u.Id == userId).Select(u => u.ManagerId).FirstOrDefaultAsync(ct);
            var ids = teamLeaderId == null
                ? new List<Guid>()
                : await db.Users.AsNoTracking()
                    .Where(u => u.ManagerId == teamLeaderId && (u.Role == 3 || u.Role == 8))
                    .Select(u => u.Id).ToListAsync(ct);
            if (!ids.Contains(userId)) ids.Add(userId);
            return ids;
        }

        public static bool CanSee(List<Guid>? visible, Guid? assignedAgentId) =>
            visible == null || (assignedAgentId.HasValue && visible.Contains(assignedAgentId.Value));
    }
}
