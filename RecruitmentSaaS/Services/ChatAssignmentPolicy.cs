using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;

namespace RecruitmentSaaS.Services;

/// <summary>Historical lead, account and handoff owners are suggestions, never authority over a chat's current owner.</summary>
public static class ChatAssignmentPolicy
{
    public static Task<Guid?> ActiveAgentAsync(RecruitmentCrmContext db, Guid? id, CancellationToken ct = default) =>
        id == null ? Task.FromResult<Guid?>(null) :
        db.Users.Where(u => u.Id == id && u.IsActive && (u.Role == 3 || u.Role == 6 || u.Role == 8))
            .Select(u => (Guid?)u.Id).SingleOrDefaultAsync(ct);

    public static async Task<Guid?> ResolveAsync(RecruitmentCrmContext db, Guid? current, Guid? suggested, CancellationToken ct = default) =>
        await ActiveAgentAsync(db, current, ct) ?? await ActiveAgentAsync(db, suggested, ct);
}
