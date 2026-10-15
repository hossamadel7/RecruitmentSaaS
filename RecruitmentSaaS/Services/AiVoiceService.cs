using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace RecruitmentSaaS.Services;

/// <summary>Audio jobs are durable and independent of the webhook queue. No audio or reply is sent.</summary>
public sealed class AiVoiceService(RecruitmentCrmContext db, AiSalesControlService controls,
    IAiVoiceMediaDownloader media, IAiVoiceTranscriber transcriber, IOptions<AiVoiceOptions> options)
{
    public AiVoiceOptions Options => options.Value;
    private void RequireEnabled()
    {
        controls.RequireEnabled();
        if (!Options.Enabled || !Options.ExternalProcessingApproved) throw new AiSalesValidation("Voice processing requires its separate external-processing approval.");
    }

    private async Task<bool> CurrentAsync(Guid chatId, long version, Guid? agentId, CancellationToken ct) =>
        controls.Options.Enabled && Options.Enabled && Options.ExternalProcessingApproved &&
        await db.ConversationControls.AnyAsync(c => c.ConversationId == chatId && c.TenantId == controls.Options.TenantId &&
            c.OwnershipVersion == version && c.Controller == ConversationControllerMode.Assisted &&
            db.WhatsAppConversations.Any(w => w.Id == c.ConversationId && w.AssignedAiSalesAgentId != null &&
                (agentId == null || w.AssignedAiSalesAgentId == agentId) &&
                db.AiSalesAgents.Any(a => a.Id == w.AssignedAiSalesAgentId && a.TenantId == c.TenantId && !a.Paused && a.Mode == AiSalesMode.Shadow &&
                    a.TeamManagerId == w.PendingTeamManagerId && a.WhatsAppAccountId == w.WhatsAppAccountId &&
                    db.WhatsAppAccounts.Any(ac => ac.Id == a.WhatsAppAccountId && ac.IsActive))), ct);

    public async Task<AiVoiceTurn[]> QueueAsync(Guid chatId, Guid actor, long version, Guid[] ids, CancellationToken ct)
    {
        RequireEnabled();
        if (ids.Length == 0 || ids.Length > Options.MaxPartsPerTurn || ids.Distinct().Count() != ids.Length)
            throw new AiSalesValidation("Select distinct recent recordings within the configured limit.");
        await using var gate = await ConversationLock.AcquireAsync(db, "conversation:" + chatId, ct);
        if (!await CurrentAsync(chatId, version, null, ct)) throw new AiSalesConflict("Voice processing requires a current resumed shadow conversation.");
        var chat = await db.WhatsAppConversations.SingleAsync(c => c.Id == chatId, ct);
        var selected = await db.WhatsAppMessages.Where(m => ids.Contains(m.Id) && m.ConversationId == chatId &&
            m.WhatsAppAccountId == chat.WhatsAppAccountId && m.Direction == (byte)MessageDirection.Incoming &&
            m.MessageType == (byte)WhatsAppMessageType.Audio && m.MediaId != null).OrderBy(m => m.WhatsAppTimestamp).ThenBy(m => m.CreatedAt).ThenBy(m => m.Id).ToListAsync(ct);
        if (selected.Count != ids.Length || selected.Any(m => m.WhatsAppTimestamp < DateTime.UtcNow.AddMinutes(-Options.MaxMessageAgeMinutes) ||
            m.WhatsAppTimestamp > DateTime.UtcNow.AddMinutes(1) || string.IsNullOrWhiteSpace(m.MediaId)))
            throw new AiSalesValidation("Only recent incoming audio from this conversation can be selected.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var result = new List<AiVoiceTurn>();
        foreach (var message in selected) result.Add(await AddLockedAsync(chat, message, version, actor, ct));
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return result.DistinctBy(t => t.Id).ToArray();
    }

    // Caller holds the conversation lock and the inbound transaction; this never calls a provider.
    public async Task QueueIncomingLockedAsync(WhatsAppConversation chat, WhatsAppMessage message, CancellationToken ct)
    {
        if (!Options.AutoQueueIncoming || !Options.Enabled || !Options.ExternalProcessingApproved || !controls.Options.Enabled ||
            message.Direction != (byte)MessageDirection.Incoming || message.MessageType != (byte)WhatsAppMessageType.Audio ||
            string.IsNullOrWhiteSpace(message.MediaId) || message.WhatsAppTimestamp < DateTime.UtcNow.AddMinutes(-Options.MaxMessageAgeMinutes) ||
            message.WhatsAppTimestamp > DateTime.UtcNow.AddMinutes(1)) return;
        var control = await db.ConversationControls.AsNoTracking().SingleOrDefaultAsync(c => c.ConversationId == chat.Id && c.TenantId == controls.Options.TenantId, ct);
        if (control == null || !await CurrentAsync(chat.Id, control.OwnershipVersion, null, ct)) return;
        await AddLockedAsync(chat, message, control.OwnershipVersion, null, ct);
    }

    private async Task<AiVoiceTurn> AddLockedAsync(WhatsAppConversation chat, WhatsAppMessage message, long version, Guid? actor, CancellationToken ct)
    {
        var existing = await db.AiVoiceParts.SingleOrDefaultAsync(p => p.MessageId == message.Id, ct);
        if (existing != null) return await db.AiVoiceTurns.SingleAsync(t => t.Id == existing.TurnId, ct);
        var pending = await db.AiVoiceTurns.Where(t => t.ConversationId == chat.Id && t.TenantId == controls.Options.TenantId &&
            t.OwnershipVersion == version && t.AgentId == chat.AssignedAiSalesAgentId && t.Status == AiVoiceStatus.Pending)
            .OrderByDescending(t => t.LastMessageAt).FirstOrDefaultAsync(ct);
        if (pending != null)
        {
            var parts = await db.AiVoiceParts.Where(p => p.TurnId == pending.Id).Select(p => p.MessageId).ToListAsync(ct);
            // Save each addition so subsequent selected recordings see it. Any intervening message splits the turn.
            var previous = await db.WhatsAppMessages.Where(m => m.ConversationId == chat.Id && m.Id != message.Id &&
                (m.WhatsAppTimestamp < message.WhatsAppTimestamp || m.WhatsAppTimestamp == message.WhatsAppTimestamp && m.CreatedAt < message.CreatedAt))
                .OrderByDescending(m => m.WhatsAppTimestamp).ThenByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Select(m => (Guid?)m.Id).FirstOrDefaultAsync(ct);
            if (parts.Count >= Options.MaxPartsPerTurn || previous == null || !parts.Contains(previous.Value) ||
                message.WhatsAppTimestamp < pending.LastMessageAt || message.WhatsAppTimestamp - pending.LastMessageAt > TimeSpan.FromSeconds(Options.GroupGapSeconds)) pending = null;
        }
        if (pending == null)
        {
            pending = new() { TenantId = controls.Options.TenantId, ConversationId = chat.Id, AgentId = chat.AssignedAiSalesAgentId!.Value,
                OwnershipVersion = version, RequestedById = actor };
            db.AiVoiceTurns.Add(pending);
            db.AuditLogs.Add(new() { Id = Guid.NewGuid(), ActorId = actor, ActorType = actor == null ? (byte)2 : (byte)1,
                EventType = "AiVoiceQueued", EntityType = "AiVoiceTurn", EntityId = pending.Id, CreatedAt = DateTime.UtcNow });
        }
        else pending.Version++;
        pending.LastMessageAt = message.WhatsAppTimestamp; pending.AvailableAt = DateTime.UtcNow.AddSeconds(Options.QuietSeconds);
        db.AiVoiceParts.Add(new() { MessageId = message.Id, TurnId = pending.Id, MediaId = message.MediaId! });
        await db.SaveChangesAsync(ct); return pending;
    }

    private async Task ProcessAsync(Guid id, CancellationToken ct)
    {
        RequireEnabled();
        var turn = await db.AiVoiceTurns.SingleAsync(t => t.Id == id && t.TenantId == controls.Options.TenantId, ct);
        await using (var gate = await ConversationLock.AcquireAsync(db, "conversation:" + turn.ConversationId, ct))
        {
            await db.Entry(turn).ReloadAsync(ct);
            if (turn.Status != AiVoiceStatus.Pending || turn.AvailableAt > DateTime.UtcNow) return;
            if (!await CurrentAsync(turn.ConversationId, turn.OwnershipVersion, turn.AgentId, ct))
            { turn.Status = AiVoiceStatus.Stale; turn.ErrorCode = "OwnershipChanged"; turn.Version++; await db.SaveChangesAsync(ct); return; }
            turn.Status = AiVoiceStatus.Processing; turn.StartedAt = DateTime.UtcNow; turn.Version++; await db.SaveChangesAsync(ct);
        }
        var parts = await db.AiVoiceParts.Where(p => p.TurnId == id).Join(db.WhatsAppMessages, p => p.MessageId, m => m.Id,
            (p, m) => new { Part = p, m.WhatsAppTimestamp, m.CreatedAt }).OrderBy(x => x.WhatsAppTimestamp).ThenBy(x => x.CreatedAt).ThenBy(x => x.Part.MessageId)
            .Select(x => x.Part).ToListAsync(ct);
        string? error = null;
        foreach (var part in parts.Where(p => p.Status != AiVoicePartStatus.Completed))
        {
            AiVoiceAudio? audio = null;
            try
            {
                // Check control before every recording, without blocking takeover during an external call.
                if (!await CurrentAsync(turn.ConversationId, turn.OwnershipVersion, turn.AgentId, ct)) throw new AiVoiceException("OwnershipChanged");
                audio = await media.DownloadAsync(part.MediaId, ct);
                if (!await CurrentAsync(turn.ConversationId, turn.OwnershipVersion, turn.AgentId, ct)) throw new AiVoiceException("OwnershipChanged");
                await using (var tx = await db.Database.BeginTransactionAsync(ct))
                {
                    await ConversationLock.LockTransactionAsync(db, "voice-budget:" + turn.TenantId, ct);
                    var day = DateTime.UtcNow.Date;
                    var attempts = await db.AiVoiceParts.Where(p => p.LastAttemptAt >= day && db.AiVoiceTurns.Any(t => t.Id == p.TurnId && t.TenantId == turn.TenantId))
                        .SumAsync(p => (int?)p.AttemptsOnLastDay, ct) ?? 0;
                    if (attempts >= Options.MaxProviderAttemptsPerDay) throw new AiVoiceException("DailyVoiceLimit");
                    part.AttemptsOnLastDay = part.LastAttemptAt >= day ? part.AttemptsOnLastDay + 1 : 1;
                    part.LastAttemptAt = DateTime.UtcNow; part.Status = AiVoicePartStatus.Processing;
                    part.MediaSha256 = audio.Sha256; part.ContentType = audio.ContentType; part.ByteCount = audio.Data.Length;
                    await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                }
                var output = await transcriber.TranscribeAsync(audio, ct);
                if (string.IsNullOrWhiteSpace(output.Text) || output.Text.Length > 2000 || output.Model.Length > 100 ||
                    output.LowestTokenProbability is double probability && (!double.IsFinite(probability) || probability < 0 || probability > 1))
                    throw new AiVoiceException("InvalidTranscript");
                part.Transcript = output.Text.Trim(); part.Model = output.Model; part.LowestTokenProbability = output.LowestTokenProbability;
                part.InputTokens = output.InputTokens; part.OutputTokens = output.OutputTokens; part.ReportedSeconds = output.ReportedSeconds;
                part.Status = AiVoicePartStatus.Completed; part.ErrorCode = null; await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                error = ex is AiVoiceException ? ex.Message : ex is OperationCanceledException ? "VoiceCancelled" : "VoiceProcessingFailed";
                part.Status = AiVoicePartStatus.Failed; part.ErrorCode = error; await db.SaveChangesAsync(CancellationToken.None); break;
            }
            finally { if (audio != null) CryptographicOperations.ZeroMemory(audio.Data); }
        }
        await using (var gate = await ConversationLock.AcquireAsync(db, "conversation:" + turn.ConversationId, CancellationToken.None))
        {
            var current = await CurrentAsync(turn.ConversationId, turn.OwnershipVersion, turn.AgentId, CancellationToken.None);
            turn.CompletedAt = DateTime.UtcNow; turn.Version++;
            turn.Transcript = string.Join("\n", parts.Where(p => p.Status == AiVoicePartStatus.Completed).Select(p => p.Transcript));
            if (turn.Transcript.Length > 4000) { turn.Transcript = ""; error = "TurnTranscriptTooLong"; }
            turn.LowestTokenProbability = parts.All(p => p.LowestTokenProbability != null) ? parts.Min(p => p.LowestTokenProbability) : null;
            turn.NeedsClarification = turn.LowestTokenProbability == null || turn.LowestTokenProbability < Options.MinimumTokenProbability ||
                Regex.IsMatch(turn.Transcript, @"\p{N}|جنيه|مرتب|راتب|ميعاد|موعد|ساعة|رسوم", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            // A spoken opt-out or request for a person immediately stops automation.
            if (current && (IntakeScript.WantsPerson(IntakeScript.Normalize(turn.Transcript)) || IntakeScript.NotInterested(IntakeScript.Normalize(turn.Transcript))))
            {
                var chat = await db.WhatsAppConversations.SingleAsync(c => c.Id == turn.ConversationId);
                await controls.TakeOverLockedAsync(chat, null, "Voice transcript requests human assistance or declines further assistance.", CancellationToken.None);
                current = false; error = "HumanRequested";
            }
            turn.Status = !current ? AiVoiceStatus.Stale : error != null ? AiVoiceStatus.Failed : AiVoiceStatus.ReadyForReview;
            turn.ErrorCode = !current ? error ?? "OwnershipChanged" : error;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    public async Task<AiVoiceTurn> ReviewAsync(Guid id, Guid actor, long version, string text, bool criticalVerified, CancellationToken ct)
    {
        var turn = await db.AiVoiceTurns.SingleAsync(t => t.Id == id && t.TenantId == controls.Options.TenantId, ct);
        await using var gate = await ConversationLock.AcquireAsync(db, "conversation:" + turn.ConversationId, ct);
        await db.Entry(turn).ReloadAsync(ct);
        if (turn.Version != version || turn.Status != AiVoiceStatus.ReadyForReview || !await CurrentAsync(turn.ConversationId, turn.OwnershipVersion, turn.AgentId, ct))
            throw new AiSalesConflict("Recording context changed or is not available for review.");
        if (string.IsNullOrWhiteSpace(text) || text.Length > 4000 || turn.NeedsClarification && !criticalVerified)
            throw new AiSalesValidation("Review the transcript and confirm uncertain or critical details before approval.");
        if (IntakeScript.WantsPerson(IntakeScript.Normalize(text)) || IntakeScript.NotInterested(IntakeScript.Normalize(text)))
        {
            var chat = await db.WhatsAppConversations.SingleAsync(c => c.Id == turn.ConversationId, ct);
            await controls.TakeOverLockedAsync(chat, actor, "Reviewed voice requests human assistance or declines assistance.", ct);
            turn.Status = AiVoiceStatus.Stale; turn.ErrorCode = "HumanRequested";
        }
        else turn.Status = AiVoiceStatus.Reviewed;
        turn.ReviewedText = text.Trim(); turn.ReviewedById = actor; turn.ReviewedAt = DateTime.UtcNow; turn.Version++;
        db.AuditLogs.Add(new() { Id = Guid.NewGuid(), ActorId = actor, ActorType = 1, EventType = "AiVoiceReviewed", EntityType = "AiVoiceTurn", EntityId = id, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(ct); return turn;
    }

    public async Task<AiVoiceTurn> RetryAsync(Guid id, Guid actor, long version, bool confirm, CancellationToken ct)
    {
        RequireEnabled();
        var turn = await db.AiVoiceTurns.SingleAsync(t => t.Id == id && t.TenantId == controls.Options.TenantId, ct);
        await using var gate = await ConversationLock.AcquireAsync(db, "conversation:" + turn.ConversationId, ct);
        await db.Entry(turn).ReloadAsync(ct);
        if (!confirm || turn.Version != version || turn.Status != AiVoiceStatus.Failed || !await CurrentAsync(turn.ConversationId, turn.OwnershipVersion, turn.AgentId, ct))
            throw new AiSalesConflict("An explicit retry confirmation and current failed recording are required.");
        if (turn.ErrorCode == "TurnTranscriptTooLong") throw new AiSalesValidation("This turn exceeds the review limit; it cannot be retried.");
        var parts = await db.AiVoiceParts.Where(p => p.TurnId == id && p.Status != AiVoicePartStatus.Completed).ToListAsync(ct);
        foreach (var part in parts) { part.Status = AiVoicePartStatus.Pending; part.ErrorCode = null; }
        turn.Status = AiVoiceStatus.Pending; turn.ErrorCode = null; turn.AvailableAt = DateTime.UtcNow; turn.StartedAt = null; turn.Version++;
        db.AuditLogs.Add(new() { Id = Guid.NewGuid(), ActorId = actor, ActorType = 1, EventType = "AiVoiceRetryConfirmed", EntityType = "AiVoiceTurn", EntityId = id, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(ct); return turn;
    }

    public async Task RunNextAsync(CancellationToken ct)
    {
        if (!Options.Enabled || !Options.ExternalProcessingApproved || !controls.Options.Enabled) return;
        // The worker lock is independent of webhook processing and makes interrupted-call recovery safe.
        await using var gate = await ConversationLock.AcquireAsync(db, "voice-worker", ct);
        var interrupted = await db.AiVoiceTurns.Where(t => t.TenantId == controls.Options.TenantId && t.Status == AiVoiceStatus.Processing).ToListAsync(ct);
        foreach (var turn in interrupted)
        { turn.Status = AiVoiceStatus.Failed; turn.ErrorCode = "InterruptedTranscription"; turn.CompletedAt = DateTime.UtcNow; turn.Version++; }
        await db.SaveChangesAsync(ct);
        var id = await db.AiVoiceTurns.Where(t => t.TenantId == controls.Options.TenantId && t.Status == AiVoiceStatus.Pending && t.AvailableAt <= DateTime.UtcNow)
            .OrderBy(t => t.AvailableAt).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        if (id != null) await ProcessAsync(id.Value, ct);
    }
}
