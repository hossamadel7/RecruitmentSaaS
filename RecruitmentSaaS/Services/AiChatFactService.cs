using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RecruitmentSaaS.Services;

public sealed record AiChatFactExtraction(AiChatCorpusPage Page, int AddedCandidates);
public sealed record AiChatFactQuestion(AiChatFactCandidate Candidate, bool SourceUnchanged, AiSalesKnowledge[] RelatedFacts, DateTime? SourceCreatedAt);
public sealed record AiChatFactDecision(long ExpectedVersion, bool Publish, string? ConfirmedTitle, string? ConfirmedContent,
    bool PrivacyReviewed, DateTime ValidUntil, AiChatFactResolution[] Resolutions);
public sealed record AiChatFactResolution(Guid KnowledgeId, long ExpectedVersion, bool Retire);

/// <summary>Local topic detection produces questions, never trusted facts. Only an owner's review publishes.</summary>
public sealed class AiChatFactService(RecruitmentCrmContext db, AiSalesControlService controls,
    AiChatLearningService corpus, IOptions<AiChatLearningOptions> options)
{
    public static readonly (string Key, string Title, string[] Terms)[] Topics = [
        ("salary", "الراتب", ["مرتب", "راتب", "دولار", "salary"]),
        ("fees", "الرسوم والدفع", ["رسوم", "تكلف", "دفع", "فلوس", "fees"]),
        ("office", "المكتب والزيارة", ["مكتب", "عنوان", "الهرم", "زيار", "office"]),
        ("work", "ساعات العمل والسكن", ["ساع", "سكن", "وجبات", "ايام", "work hours"]),
        ("documents", "الأوراق والإجراءات", ["جواز", "اوراق", "مستند", "تاشير", "passport"]),
        ("timing", "مدة الإجراءات", ["اسبوع", "مده", "سفر", "deadline"]),
        ("roles", "الوظائف والخبرة", ["وظيف", "مصانع", "لحام", "خبره", "vacancy"]),
        ("age", "السن المعلن", ["سن", "عمر", "age"])
    ];
    private Guid Tenant => controls.Options.TenantId;
    private void Require(Guid account)
    {
        controls.RequireEnabled();
        if (!options.Value.Enabled || !options.Value.ApprovedAccountIds.Contains(account)) throw new AiSalesValidation("Chat learning account unavailable.");
    }
    public static string Fingerprint(WhatsAppMessage m) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(
        new { m.Id, m.ConversationId, m.WhatsAppAccountId, m.MessageType, m.Direction, m.MessageSource,
            ConfirmedSend = m.Status is (byte)MessageStatus.Sent or (byte)MessageStatus.Delivered or (byte)MessageStatus.Read,
            m.TextBody, m.SenderUserId, m.SenderAiSalesAgentId }))));
    private static bool Matches(string text, string key)
    {
        var normalized = IntakeScript.Normalize(text);
        return Topics.Single(t => t.Key == key).Terms.Any(term => normalized.Contains(IntakeScript.Normalize(term), StringComparison.Ordinal));
    }
    public async Task<AiChatFactExtraction> ExtractAsync(Guid account, Guid? after, DateTime? snapshot, int pageSize, Guid actor, CancellationToken ct)
    {
        Require(account);
        await using var gate = await ConversationLock.AcquireAsync(db, "chat-fact-extract:" + Tenant + ":" + account, ct);
        var page = await corpus.PrepareAsync(account, after, snapshot, pageSize, actor, ct);
        var ids = page.Items.Where(i => i.CandidateForReview && i.MessageType == "Text").Select(i => i.InternalMessageReference).ToArray();
        var sources = await db.WhatsAppMessages.AsNoTracking().Where(m => ids.Contains(m.Id) && m.WhatsAppAccountId == account).ToListAsync(ct);
        var existing = await db.AiChatFactCandidates.AsNoTracking().Where(c => c.TenantId == Tenant && c.WhatsAppAccountId == account && ids.Contains(c.SourceMessageId!.Value)).ToListAsync(ct);
        var count = 0;
        foreach (var item in page.Items.Where(i => ids.Contains(i.InternalMessageReference)))
        {
            var source = sources.SingleOrDefault(m => m.Id == item.InternalMessageReference);
            // Recheck the redacted body against current source. A concurrently edited page must be re-extracted.
            if (source == null || source.MessageType != (byte)WhatsAppMessageType.Text) continue;
            var fingerprint = Fingerprint(source);
            if (fingerprint != item.SourceFingerprint) continue;
            foreach (var topic in Topics.Where(t => Matches(item.ReviewText!, t.Key)))
            {
                if (existing.Any(c => c.SourceMessageId == source.Id && c.SourceFingerprint == fingerprint && c.Topic == topic.Key)) continue;
                db.AiChatFactCandidates.Add(new() { TenantId = Tenant, WhatsAppAccountId = account, SourceMessageId = source.Id,
                    SourceFingerprint = fingerprint, Topic = topic.Key, Speaker = item.Speaker, SourceStatement = item.ReviewText! }); count++;
            }
        }
        await db.SaveChangesAsync(ct); return new(page, count);
    }
    private async Task<AiSalesKnowledge[]> RelatedAsync(AiChatFactCandidate c, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var facts = await db.AiSalesKnowledge.AsNoTracking().Where(k => k.TenantId == Tenant && k.WhatsAppAccountId == c.WhatsAppAccountId
            && k.Kind == AiKnowledgeKind.Fact && k.Status == AiKnowledgeStatus.Approved && k.ValidFrom <= now && k.ValidUntil > now)
            .OrderBy(k => k.Id).Take(501).ToListAsync(ct);
        if (facts.Count > 500) throw new AiSalesConflict("More than 500 current facts; narrow/review account knowledge first.");
        var factIds = facts.Select(k => k.Id).ToArray();
        var reviewedIds = await db.AiChatFactCandidates.AsNoTracking().Where(r => r.TenantId == Tenant && r.WhatsAppAccountId == c.WhatsAppAccountId
            && r.Topic == c.Topic && r.State == AiChatFactState.Published && r.KnowledgeId != null && factIds.Contains(r.KnowledgeId.Value))
            .Select(r => r.KnowledgeId!.Value).ToListAsync(ct);
        return facts.Where(k => reviewedIds.Contains(k.Id) || Matches(k.Title + " " + k.Content, c.Topic)).ToArray();
    }
    public async Task<AiChatFactQuestion?> NextAsync(Guid account, CancellationToken ct)
    {
        Require(account);
        var candidate = await db.AiChatFactCandidates.AsNoTracking().Where(c => c.TenantId == Tenant && c.WhatsAppAccountId == account && c.State == AiChatFactState.Pending)
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).FirstOrDefaultAsync(ct);
        if (candidate == null) return null;
        var source = await db.WhatsAppMessages.AsNoTracking().SingleOrDefaultAsync(m => m.Id == candidate.SourceMessageId && m.WhatsAppAccountId == account, ct);
        return new(candidate, source != null && Fingerprint(source) == candidate.SourceFingerprint, await RelatedAsync(candidate, ct),
            source == null ? null : DateTime.SpecifyKind(source.CreatedAt, DateTimeKind.Utc));
    }
    public async Task<AiChatFactCandidate> DecideAsync(Guid id, Guid actor, AiChatFactDecision decision, CancellationToken ct)
    {
        await using var gate = await ConversationLock.AcquireAsync(db, "chat-fact-review:" + id, ct);
        var candidate = await db.AiChatFactCandidates.SingleOrDefaultAsync(c => c.Id == id && c.TenantId == Tenant, ct)
            ?? throw new AiSalesValidation("Candidate unavailable.");
        Require(candidate.WhatsAppAccountId);
        if (candidate.Version != decision.ExpectedVersion || candidate.State != AiChatFactState.Pending) throw new AiSalesConflict("Candidate already reviewed or changed.");
        await using var accountGate = await ConversationLock.AcquireAsync(db, "knowledge-account:" + Tenant + ":" + candidate.WhatsAppAccountId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (decision.Publish)
        {
            var source = await db.WhatsAppMessages.FromSqlInterpolated($"SELECT * FROM demorecruitment.\"WhatsAppMessages\" WHERE \"Id\" = {candidate.SourceMessageId} AND \"WhatsAppAccountId\" = {candidate.WhatsAppAccountId} FOR SHARE")
                .AsNoTracking().SingleOrDefaultAsync(ct);
            if (source == null || Fingerprint(source) != candidate.SourceFingerprint) throw new AiSalesConflict("Source changed or was deleted; reject and re-extract.");
            var now = DateTime.UtcNow;
            if (!decision.PrivacyReviewed || string.IsNullOrWhiteSpace(decision.ConfirmedTitle) || decision.ConfirmedTitle.Length > 150 ||
                string.IsNullOrWhiteSpace(decision.ConfirmedContent) || decision.ConfirmedContent.Length > 4000 || decision.ValidUntil.Kind == DateTimeKind.Unspecified ||
                decision.ValidUntil.ToUniversalTime() <= now || decision.ValidUntil.ToUniversalTime() > now.AddDays(90))
                throw new AiSalesValidation("Review privacy, enter the correct title/fact, and choose a UTC expiry within 90 days.");
            var related = await RelatedAsync(candidate, ct);
            if (decision.Resolutions.Select(r => r.KnowledgeId).Distinct().Count() != decision.Resolutions.Length ||
                !related.Select(k => k.Id).ToHashSet().SetEquals(decision.Resolutions.Select(r => r.KnowledgeId)))
                throw new AiSalesConflict("Review every currently related fact and decide explicitly whether to retire or retain it.");
            foreach (var r in decision.Resolutions.OrderBy(r => r.KnowledgeId))
            {
                await ConversationLock.LockTransactionAsync(db, "knowledge:" + r.KnowledgeId, ct);
                var fact = await db.AiSalesKnowledge.SingleAsync(k => k.Id == r.KnowledgeId && k.TenantId == Tenant && k.WhatsAppAccountId == candidate.WhatsAppAccountId, ct);
                await db.Entry(fact).ReloadAsync(ct);
                if (fact.Version != r.ExpectedVersion || fact.Status != AiKnowledgeStatus.Approved) throw new AiSalesConflict("Related fact changed; refresh the question.");
                if (r.Retire) { fact.Status = AiKnowledgeStatus.Retired; fact.Version++; }
            }
            var knowledge = new AiSalesKnowledge { TenantId = Tenant, WhatsAppAccountId = candidate.WhatsAppAccountId, Kind = AiKnowledgeKind.Fact,
                Title = decision.ConfirmedTitle.Trim(), Content = decision.ConfirmedContent.Trim(), SourceReference = "Owner reviewed chat candidate " + candidate.Id,
                Status = AiKnowledgeStatus.Approved, ValidFrom = now.AddMinutes(-1), ValidUntil = decision.ValidUntil.ToUniversalTime(),
                CreatedById = actor, ApprovedById = actor, ApprovedAt = now };
            db.AiSalesKnowledge.Add(knowledge); candidate.KnowledgeId = knowledge.Id; candidate.State = AiChatFactState.Published;
        }
        else candidate.State = AiChatFactState.Rejected;
        candidate.Version++; candidate.ReviewedById = actor; candidate.ReviewedAt = DateTime.UtcNow;
        db.AuditLogs.Add(new() { Id = Guid.NewGuid(), ActorId = actor, ActorType = 1, EventType = decision.Publish ? "AiChatFactPublished" : "AiChatFactRejected",
            EntityType = "AiChatFactCandidate", EntityId = id, CreatedAt = DateTime.UtcNow,
            NewValueJson = JsonSerializer.Serialize(new { candidate.KnowledgeId, candidate.Version, decision.PrivacyReviewed, decision.Resolutions }) });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return candidate;
    }
}
