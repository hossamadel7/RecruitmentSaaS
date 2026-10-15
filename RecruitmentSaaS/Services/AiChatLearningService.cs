using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace RecruitmentSaaS.Services;

public sealed class AiChatLearningOptions
{
    public bool Enabled { get; set; }
    public Guid[] ApprovedAccountIds { get; set; } = [];
    // Server-side secret, separate from OpenAI/Meta credentials. Never returned to clients.
    public string PseudonymKey { get; set; } = "";
}
public sealed record AiChatCorpusItem(Guid InternalMessageReference, string ConversationToken, string Speaker,
    string MessageType, DateTime CreatedAt, string? ReviewText, bool CandidateForReview, string[] ReviewFlags, string SourceFingerprint);
public sealed record AiChatCorpusPage(Guid AccountId, DateTime SnapshotBefore, Guid? NextAfterMessageId,
    bool HasMore, bool TrainingReady, string ReviewStatus, AiChatCorpusItem[] Items);

/// <summary>Local preparation only. Never publishes knowledge, pairs replies, uploads or trains a model.</summary>
public sealed class AiChatLearningService(RecruitmentCrmContext db, AiSalesControlService controls, IOptions<AiChatLearningOptions> options)
{
    public async Task<AiChatCorpusPage> PrepareAsync(Guid accountId, Guid? after, DateTime? snapshot, int pageSize, Guid actor, CancellationToken ct)
    {
        controls.RequireEnabled(); var settings = options.Value;
        if (!settings.Enabled || !settings.ApprovedAccountIds.Contains(accountId) || Encoding.UTF8.GetByteCount(settings.PseudonymKey) < 32)
            throw new AiSalesValidation("Local chat preparation is disabled or this account has not been selected.");
        if (pageSize is < 1 or > 300) throw new AiSalesValidation("Page size must be between 1 and 300 messages.");
        if (after != null && snapshot == null) throw new AiSalesValidation("Continuation requires the original snapshot boundary.");
        var now = DateTime.UtcNow;
        var boundary = snapshot ?? new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        if (boundary.Kind == DateTimeKind.Unspecified || boundary.ToUniversalTime() > now)
            throw new AiSalesValidation("Provide the UTC snapshot returned by the first page.");
        boundary = boundary.ToUniversalTime();
        if (!await db.WhatsAppAccounts.AnyAsync(a => a.Id == accountId && a.IsActive, ct))
            throw new AiSalesValidation("Account unavailable.");
        // UUID keyset visits all stored message IDs without a per-conversation history limit.
        // It is not chronological ordering; reviewers reconstruct turns by conversation/time/reference.
        var cursor = after ?? Guid.Empty;
        var messages = await db.WhatsAppMessages.FromSqlInterpolated($"""
            SELECT m.* FROM demorecruitment."WhatsAppMessages" m
            WHERE m."WhatsAppAccountId" = {accountId} AND m."CreatedAt" <= {boundary} AND m."Id" > {cursor}
            ORDER BY m."Id" LIMIT {pageSize + 1}
            """).AsNoTracking().ToListAsync(ct);
        var hasMore = messages.Count > pageSize; messages = messages.Take(pageSize).ToList();
        var chatIds = messages.Select(m => m.ConversationId).Distinct().ToArray();
        var chats = await db.WhatsAppConversations.AsNoTracking().Where(c => chatIds.Contains(c.Id) && c.WhatsAppAccountId == accountId)
            .Select(c => new { c.Id, ContactName = c.Contact.Name, LeadName = c.Lead == null ? null : c.Lead.FullName }).ToListAsync(ct);
        var users = messages.Where(m => m.SenderUserId != null).Select(m => m.SenderUserId!.Value).Distinct().ToArray();
        var names = await db.Users.AsNoTracking().Where(u => users.Contains(u.Id)).Select(u => new { u.Id, u.FullName }).ToListAsync(ct);
        var messageIds = messages.Select(m => m.Id).ToArray();
        var transcripts = await db.AiVoiceParts.AsNoTracking().Where(p => messageIds.Contains(p.MessageId))
            .Join(db.AiVoiceTurns.AsNoTracking().Where(t => t.TenantId == controls.Options.TenantId && t.Status == AiVoiceStatus.Reviewed),
                p => p.TurnId, t => t.Id, (p, t) => new { p.MessageId, t.ConversationId, t.ReviewedText, t.Id }).ToListAsync(ct);
        var result = new List<AiChatCorpusItem>();
        foreach (var m in messages)
        {
            var chat = chats.SingleOrDefault(c => c.Id == m.ConversationId);
            if (chat == null) throw new AiSalesValidation("Inconsistent message/account scope; preparation stopped.");
            var incoming = m.Direction == (byte)MessageDirection.Incoming;
            var ai = m.SenderAiSalesAgentId != null || m.MessageSource == (byte)MessageSource.System;
            var speaker = incoming ? "customer" : ai ? "ai_or_system" : m.SenderUserId != null ? "staff" : "staff_or_device";
            var flags = new List<string> { "ManualPrivacyReviewRequired", "HistoricalClaimsNotCurrentFacts", "QualityAndContextReviewRequired", "MarketLabelRequired" };
            string? text = null;
            if (m.MessageType == (byte)WhatsAppMessageType.Text) text = m.TextBody;
            else if (m.MessageType == (byte)WhatsAppMessageType.Audio)
            {
                var voice = transcripts.SingleOrDefault(t => t.MessageId == m.Id && t.ConversationId == m.ConversationId);
                if (voice != null) { text = voice.ReviewedText; flags.Add("ReviewedVoiceGroup:" + voice.Id); flags.Add("GroupTranscriptMayRepeatAcrossRecordings"); }
                else flags.Add("AudioNeedsTranscriptionAndReview");
            }
            else flags.Add("MediaNeedsSeparateInterpretation");
            var eligible = !string.IsNullOrWhiteSpace(text) && !ai && (incoming || m.Status is (byte)MessageStatus.Sent or (byte)MessageStatus.Delivered or (byte)MessageStatus.Read);
            if (ai) flags.Add("DoNotUseAsHumanTrainingTarget");
            if (!incoming && m.Status is not ((byte)MessageStatus.Sent or (byte)MessageStatus.Delivered or (byte)MessageStatus.Read)) flags.Add("UnconfirmedOrFailedStaffSend");
            if (speaker == "staff_or_device") flags.Add("HumanAuthorshipNeedsReview");
            if (text != null)
            {
                if (text.Length > 4000) { flags.Add("TextTruncated"); text = text[..4000]; eligible = false; }
                text = Mask(text, [chat.ContactName, chat.LeadName, names.SingleOrDefault(u => u.Id == m.SenderUserId)?.FullName]);
                if (IntakeScript.NotInterested(IntakeScript.Normalize(text))) flags.Add("PossibleOptOut");
            }
            var token = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(settings.PseudonymKey), Encoding.UTF8.GetBytes(m.ConversationId.ToString("N"))));
            result.Add(new(m.Id, token, speaker, ((WhatsAppMessageType)m.MessageType).ToString(), m.CreatedAt, text, eligible, flags.ToArray(), AiChatFactService.Fingerprint(m)));
        }
        // Audit selection and pagination, never text or masking secret.
        db.AuditLogs.Add(new() { Id = Guid.NewGuid(), ActorId = actor, ActorType = 1, EventType = "AiChatCorpusPrepared",
            EntityType = "WhatsAppAccount", EntityId = accountId, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(ct);
        return new(accountId, boundary, hasMore ? messages.Last().Id : null, hasMore, false, "UnreviewedLocalPreparationOnly", result.ToArray());
    }

    public static string Mask(string text, IEnumerable<string?> knownNames)
    {
        const RegexOptions rules = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
        var timeout = TimeSpan.FromSeconds(1);
        text = Regex.Replace(text, @"https?://[^\s<>]+", "[link removed]", rules, timeout);
        text = Regex.Replace(text, @"[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}", "[email removed]", rules, timeout);
        text = Regex.Replace(text, @"(?<!\p{N})(?:\+?\p{N}[\s()-]*){7,}(?!\p{N})", "[identifier removed]", rules, timeout);
        // Mask known full names and substantial individual name tokens; unknown names/addresses need review.
        var names = knownNames.Where(n => !string.IsNullOrWhiteSpace(n)).SelectMany(n => new[] { n!.Trim() }.Concat(n.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
            .Where(n => n.Length >= 3).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(n => n.Length);
        foreach (var name in names)
            text = Regex.Replace(text, @"(?<!\p{L})" + Regex.Escape(name) + @"(?!\p{L})", "[name removed]", rules, timeout);
        return text;
    }
}
