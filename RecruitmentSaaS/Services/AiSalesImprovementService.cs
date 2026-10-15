using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RecruitmentSaaS.Services;

public sealed record AiImprovementCase(Guid DraftId, Guid ConversationId, AiDraftStatus Status, string Reply,
    string ReviewNote, DateTime CreatedAt, string SourceFingerprint, string OutcomeEvidence, int VerifiedVisits);
public sealed record AiImprovementRequest(Guid SourceDraftId, string ExpectedSourceFingerprint,
    string Title, string ExampleQuestion, string ProposedApproach, string Reason, bool PrivacyReviewed);

/// <summary>Observed drafts lead to pending style examples only. Outcome evidence never certifies a successful sales method.</summary>
public sealed class AiSalesImprovementService(RecruitmentCrmContext db, AiSalesControlService controls)
{
    private Guid Tenant => controls.Options.TenantId;
    // PostgreSQL's legacy timestamps lose Kind and sub-microsecond precision; hash the persisted precision.
    public static string Fingerprint(AiSalesDraft draft) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new { draft.Id, draft.TenantId, draft.ConversationId, draft.AgentId, draft.Status,
            draft.Reply, draft.Intent, draft.ReviewNote, CreatedAt = draft.CreatedAt.Ticks / 10, draft.ReviewedById,
            ReviewedAt = draft.ReviewedAt?.Ticks / 10, CompletedAt = draft.CompletedAt?.Ticks / 10 }))));

    public async Task<AiImprovementCase[]> ListAsync(Guid account, CancellationToken ct)
    {
        controls.RequireEnabled();
        if (!await db.WhatsAppAccounts.AnyAsync(a => a.Id == account && a.IsActive, ct)) throw new AiSalesValidation("Account unavailable.");
        var drafts = await db.AiSalesDrafts.AsNoTracking().Where(d => d.TenantId == Tenant &&
            (d.Status == AiDraftStatus.Reviewed || d.Status == AiDraftStatus.Rejected || d.Status == AiDraftStatus.Failed))
            .Join(db.WhatsAppConversations.AsNoTracking().Where(c => c.WhatsAppAccountId == account), d => d.ConversationId, c => c.Id, (d, c) => d)
            .OrderByDescending(d => d.CreatedAt).ThenByDescending(d => d.Id).Take(20).ToListAsync(ct);
        var chats = drafts.Select(d => d.ConversationId).Distinct().ToArray();
        var bookings = await db.AiOfficeBookings.AsNoTracking().Where(b => b.TenantId == Tenant && chats.Contains(b.ConversationId))
            .Select(b => new { b.ConversationId, b.AgentId, b.LeadId, b.BranchId, b.Status, b.CreatedAt, b.ArrivalLocal, b.VerifiedVisitId }).ToListAsync(ct);
        var visitIds = bookings.Where(b => b.VerifiedVisitId != null).Select(b => b.VerifiedVisitId!.Value).ToArray();
        var visits = await db.LeadVisits.AsNoTracking().Where(v => visitIds.Contains(v.Id))
            .Select(v => new { v.Id, v.LeadId, v.BranchId, v.VisitDateTime }).ToListAsync(ct);
        var now = DateTime.UtcNow;
        return drafts.Select(d =>
        {
            var related = bookings.Where(b => b.ConversationId == d.ConversationId && b.AgentId == d.AgentId && b.CreatedAt >= d.CreatedAt).ToArray();
            var verified = related.Count(b => b.Status == AiOfficeBookingStatus.Visited &&
                visits.Any(v => v.Id == b.VerifiedVisitId && v.LeadId == b.LeadId && v.BranchId == b.BranchId && v.VisitDateTime >= d.CreatedAt &&
                    v.VisitDateTime <= now.AddSeconds(1) && EgyptTime.FromUtc(v.VisitDateTime).Date == b.ArrivalLocal.Date));
            var evidence = verified > 0 ? "VerifiedVisit" : related.Any(b => b.Status == AiOfficeBookingStatus.Confirmed || b.Status == AiOfficeBookingStatus.Visited)
                ? "BookedAttendanceUnknown" : related.Any(b => b.Status == AiOfficeBookingStatus.Cancelled) ? "CancelledAttendanceUnknown" : "Unknown";
            return new AiImprovementCase(d.Id, d.ConversationId, d.Status, d.Reply, d.ReviewNote, d.CreatedAt, Fingerprint(d), evidence, verified);
        }).ToArray();
    }

    public async Task<AiSalesKnowledge> ProposeAsync(AiImprovementRequest request, Guid actor, string requestKey, CancellationToken ct)
    {
        controls.RequireEnabled();
        if (string.IsNullOrWhiteSpace(requestKey) || requestKey.Length > 128 || !request.PrivacyReviewed ||
            request.ExpectedSourceFingerprint?.Length != 64 || !request.ExpectedSourceFingerprint.All(Uri.IsHexDigit) ||
            string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 150 ||
            string.IsNullOrWhiteSpace(request.ExampleQuestion) || request.ExampleQuestion.Length > 500 ||
            string.IsNullOrWhiteSpace(request.ProposedApproach) || request.ProposedApproach.Length > 2500 ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 300)
            throw new AiSalesValidation("Review privacy and enter a title, anonymized example, approach and reason within their limits.");
        var draft = await db.AiSalesDrafts.AsNoTracking().SingleOrDefaultAsync(d => d.Id == request.SourceDraftId && d.TenantId == Tenant, ct)
            ?? throw new AiSalesValidation("Scoped source draft unavailable.");
        var account = await db.WhatsAppConversations.Where(c => c.Id == draft.ConversationId).Select(c => c.WhatsAppAccountId).SingleAsync(ct);
        if (!await db.WhatsAppAccounts.AnyAsync(a => a.Id == account && a.IsActive, ct)) throw new AiSalesValidation("Account unavailable.");
        await using var accountGate = await ConversationLock.AcquireAsync(db, "knowledge-account:" + Tenant + ":" + account, ct);
        var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("improvement:" + Tenant + ":" + account + ":" + actor + ":" + requestKey))[..16]);
        var content = "مثال أسلوب للمراجعة، وليس مصدرًا لحقائق الشركة أو إثباتًا لنتيجة بيع.\nسؤال العميل المنقح: " + request.ExampleQuestion.Trim() +
            "\nالأسلوب أو الرد المقترح: " + request.ProposedApproach.Trim();
        var reference = "Staff improvement from draft " + draft.Id + "; source " + request.ExpectedSourceFingerprint + "; reason: " + request.Reason.Trim();
        var existing = await db.AiSalesKnowledge.SingleOrDefaultAsync(k => k.Id == id, ct);
        if (existing != null)
        {
            if (existing.TenantId != Tenant || existing.WhatsAppAccountId != account || existing.CreatedById != actor ||
                existing.Title != request.Title.Trim() || existing.Content != content || existing.SourceReference != reference)
                throw new AiSalesConflict("This improvement key was already used for different content.");
            return existing;
        }
        await using var chatGate = await ConversationLock.AcquireAsync(db, "conversation:" + draft.ConversationId, ct);
        draft = await db.AiSalesDrafts.AsNoTracking().SingleAsync(d => d.Id == draft.Id && d.TenantId == Tenant, ct);
        if (draft.Status is not (AiDraftStatus.Reviewed or AiDraftStatus.Rejected or AiDraftStatus.Failed) || Fingerprint(draft) != request.ExpectedSourceFingerprint)
            throw new AiSalesConflict("Source draft changed; refresh before suggesting an improvement.");
        var item = new AiSalesKnowledge { Id = id, TenantId = Tenant, WhatsAppAccountId = account, Kind = AiKnowledgeKind.Playbook,
            Status = AiKnowledgeStatus.Draft, Title = request.Title.Trim(), Content = content, SourceReference = reference,
            ValidFrom = DateTime.UtcNow, ValidUntil = DateTime.UtcNow.AddDays(7), CreatedById = actor };
        db.AiSalesKnowledge.Add(item);
        db.AuditLogs.Add(new() { Id = Guid.NewGuid(), ActorId = actor, ActorType = 1, EventType = "AiImprovementProposed",
            EntityType = "AiSalesKnowledge", EntityId = item.Id, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
        return item;
    }
}
