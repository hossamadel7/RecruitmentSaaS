using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using System.Security.Cryptography;

namespace RecruitmentSaaS.Services;

/// <summary>
/// Turns every incoming voice note into text, stored as the message's text so the inbox, the intake
/// assistant and the AI sales replies read it like a written message. A failed voice note gets an
/// empty text ("") so it is not retried and the AI falls back to asking the customer to write.
/// </summary>
public sealed class VoiceTranscriptionService(RecruitmentCrmContext db, AiSalesControlService controls,
    IAiVoiceMediaDownloader media, IAiVoiceTranscriber transcriber, IInboxRealtimeNotifier notifier,
    IOptions<AiVoiceOptions> options, ILogger<VoiceTranscriptionService> logger)
{
    /// <summary>How long replies wait for a voice note's text before answering without it.</summary>
    public static readonly TimeSpan WaitLimit = TimeSpan.FromMinutes(2);

    public static bool Active(AiVoiceOptions o) => o.Enabled && o.ExternalProcessingApproved && o.TranscribeAllIncoming;

    /// <summary>True while an incoming voice note is still waiting for its text.</summary>
    public static bool IsPending(AiVoiceOptions? o, WhatsAppMessage m, DateTime now) =>
        o != null && Active(o) && m.Direction == (byte)MessageDirection.Incoming && m.MessageType == (byte)WhatsAppMessageType.Audio &&
        m.TextBody == null && m.MediaId != null && m.CreatedAt > now - WaitLimit;

    public static Task<bool> AnyPendingAsync(RecruitmentCrmContext db, AiVoiceOptions? o, Guid conversationId, CancellationToken ct)
    {
        if (o == null || !Active(o)) return Task.FromResult(false);
        var since = DateTime.UtcNow - WaitLimit;
        return db.WhatsAppMessages.AnyAsync(m => m.ConversationId == conversationId && m.Direction == (byte)MessageDirection.Incoming &&
            m.MessageType == (byte)WhatsAppMessageType.Audio && m.TextBody == null && m.MediaId != null && m.CreatedAt > since, ct);
    }

    /// <summary>Transcribes the oldest waiting voice notes. Returns how many were handled.</summary>
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var o = options.Value;
        if (!Active(o)) return 0;
        await using var gate = await ConversationLock.AcquireAsync(db, "voice-transcription-worker", ct);
        var since = DateTime.UtcNow.AddMinutes(-o.MaxMessageAgeMinutes);
        var ids = await db.WhatsAppMessages.AsNoTracking().Where(m => m.Direction == (byte)MessageDirection.Incoming &&
                m.MessageType == (byte)WhatsAppMessageType.Audio && m.TextBody == null && m.MediaId != null && m.CreatedAt >= since)
            .OrderBy(m => m.CreatedAt).Select(m => m.Id).Take(5).ToListAsync(ct);
        foreach (var id in ids) await TranscribeAsync(id, ct);
        return ids.Count;
    }

    private async Task TranscribeAsync(Guid messageId, CancellationToken ct)
    {
        var o = options.Value;
        var message = await db.WhatsAppMessages.AsNoTracking().SingleAsync(m => m.Id == messageId, ct);
        string text = ""; string? error = null; AiVoiceAudio? audio = null;
        try
        {
            var day = DateTime.UtcNow.Date;
            if (await db.AuditLogs.CountAsync(a => a.EventType == "VoiceTranscribed" && a.CreatedAt >= day, ct) >= o.MaxProviderAttemptsPerDay)
                throw new AiVoiceException("DailyVoiceLimit");
            audio = await media.DownloadAsync(message.MediaId!, ct);
            var output = await transcriber.TranscribeAsync(audio, ct);
            text = (output.Text ?? "").Trim();
            if (text.Length > 2000) text = text[..2000];
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { error = ex is AiVoiceException ? ex.Message : ex.GetType().Name; text = ""; }
        finally { if (audio != null) CryptographicOperations.ZeroMemory(audio.Data); }

        Guid? assignedAgent;
        await using (var lockHandle = await ConversationLock.AcquireAsync(db, "conversation:" + message.ConversationId, ct))
        {
            db.ChangeTracker.Clear();
            var stored = await db.WhatsAppMessages.SingleAsync(m => m.Id == messageId, ct);
            if (stored.TextBody != null) return; // Already handled.
            stored.TextBody = text;
            db.AuditLogs.Add(new() { Id = Guid.NewGuid(), ActorType = 2, EventType = error == null ? "VoiceTranscribed" : "VoiceTranscriptionFailed",
                EntityType = "WhatsAppMessage", EntityId = messageId, NewValueJson = error == null ? null : $"{{\"error\":\"{error[..Math.Min(error.Length, 80)]}\"}}",
                CreatedAt = DateTime.UtcNow });

            var chat = await db.WhatsAppConversations.SingleAsync(c => c.Id == message.ConversationId, ct);
            assignedAgent = chat.AssignedSalesAgentId;
            // A spoken request for a person, or "not interested", stops the AI like the same words typed.
            if (text.Length > 0 && chat.AssignedAiSalesAgentId != null)
            {
                var normalized = IntakeScript.Normalize(text);
                if (IntakeScript.WantsPerson(normalized) || IntakeScript.NotInterested(normalized))
                    await controls.TakeOverLockedAsync(chat, null, "Customer requested a person or declined further assistance in a voice note.", ct);
            }
            // The intake assistant waited for this text; let it take its turn now.
            if (chat.IntakeStatus == (byte)IntakeStatus.Collecting)
                db.DurableWorkItems.Add(new() { Kind = "Intake", ConversationId = chat.Id,
                    DeduplicationKey = "intake-voice:" + messageId, AvailableAt = DateTime.UtcNow });
            await db.SaveChangesAsync(ct);
        }
        if (error != null) logger.LogWarning("Voice note {MessageId} was not transcribed ({Error})", messageId, error);
        try { await notifier.MessageTranscribedAsync(messageId, message.ConversationId, text, assignedAgent); }
        catch (Exception) { /* The inbox shows it on the next load. */ }
    }
}

public sealed class VoiceTranscriptionWorker(IServiceScopeFactory scopes, ILogger<VoiceTranscriptionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var handled = 0;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                handled = await scope.ServiceProvider.GetRequiredService<VoiceTranscriptionService>().RunAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Voice transcription cycle failed ({ErrorType})", ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(handled > 0 ? 0.2 : 2), ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
        }
    }
}
