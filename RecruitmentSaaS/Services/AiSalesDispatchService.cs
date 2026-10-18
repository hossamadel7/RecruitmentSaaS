using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Services;

/// <summary>Explicit staff dispatch only. No timer, autonomous trigger, template, media or retry.</summary>
public sealed class AiSalesDispatchService(RecruitmentCrmContext db, AiSalesControlService controls,
    AiSalesDraftService drafts, IWhatsAppCloudApiService transport, IInboxRealtimeNotifier notifier,
    IOptions<AiSalesDispatchOptions> options)
{
    private void RequireRelease(Guid account, Guid conversation)
    {
        controls.RequireEnabled();
        var o = options.Value;
        if (!o.Enabled || !o.ReleaseApproved || !o.ApprovedAccountIds.Contains(account) ||
            !o.ApprovedConversationIds.Contains(conversation) || o.MaxAttemptsPerDay is < 1 or > 1000)
            throw new AiSalesValidation("Reviewed dispatch has not been released for this account.");
    }

    public async Task<AiOutboundIntent> QueueAsync(Guid draftId, Guid actor, long version, string key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128) throw new AiSalesValidation("Idempotency-Key is required (maximum 128 characters).");
        var tenant = controls.Options.TenantId;
        await using var requestGate = await ConversationLock.AcquireAsync(db, "dispatch-request:" + tenant + ":" + key, ct);
        var previous = await db.AiOutboundIntents.AsNoTracking().SingleOrDefaultAsync(i => i.TenantId == tenant && i.OperationKey == key, ct);
        if (previous != null)
        {
            if (previous.DraftId != draftId || previous.RequestedById != actor || previous.OwnershipVersion != version)
                throw new AiSalesConflict("Idempotency key already used for another dispatch request.");
            return previous;
        }
        var draft = await db.AiSalesDrafts.AsNoTracking().SingleOrDefaultAsync(d => d.Id == draftId && d.TenantId == tenant, ct)
            ?? throw new AiSalesValidation("Draft unavailable.");
        await using var gate = await ConversationLock.AcquireAsync(db, "conversation:" + draft.ConversationId, ct);
        var chat = await db.WhatsAppConversations.AsNoTracking().SingleAsync(c => c.Id == draft.ConversationId, ct);
        RequireRelease(chat.WhatsAppAccountId, chat.Id);
        if (draft.OwnershipVersion != version || !await EligibleAsync(draft, ct))
            throw new AiSalesConflict("A current, reviewed reply without a human escalation is required.");
        if (await db.AiOutboundIntents.AnyAsync(i => i.DraftId == draftId, ct))
            throw new AiSalesConflict("This draft already has a dispatch record; inspect it before proceeding.");
        if (await db.AiOutboundIntents.AnyAsync(i => i.TenantId == tenant && i.ConversationId == chat.Id &&
            (i.State == "Queued" || i.State == "Sending" || i.State == "Unknown"), ct))
            throw new AiSalesConflict("Resolve the existing dispatch before queuing another reply.");
        await RequireWindowAsync(chat.Id, ct);
        var intent = new AiOutboundIntent { TenantId = tenant, ConversationId = chat.Id, DraftId = draftId,
            RequestedById = actor, OwnershipVersion = version, OperationKey = key, State = "Queued" };
        db.AiOutboundIntents.Add(intent); Audit(intent, actor, "AiDispatchQueued"); await db.SaveChangesAsync(ct);
        return intent;
    }

    private async Task<bool> EligibleAsync(AiSalesDraft d, CancellationToken ct) =>
        d.Status == AiDraftStatus.Reviewed && d.ReviewedById != null && d.ReviewedAt != null && !d.NeedsHuman &&
        d.Intent is "answer" or "acknowledgement" or "qualification" or "consultation" && !string.IsNullOrWhiteSpace(d.Reply) &&
        d.Reply.Length <= 3000 && await drafts.IsCurrentAsync(d, ct);

    private async Task RequireWindowAsync(Guid chat, CancellationToken ct)
    {
        var last = await db.WhatsAppMessages.AsNoTracking().Where(m => m.ConversationId == chat && m.Direction == (byte)MessageDirection.Incoming)
            .MaxAsync(m => (DateTime?)m.WhatsAppTimestamp, ct);
        var now = DateTime.UtcNow;
        // Legacy timestamp columns round to whole seconds; tolerate only that rounding.
        if (last == null || last < now.AddHours(-24).AddMinutes(5) || last > now.AddSeconds(1))
            throw new AiSalesValidation("Customer reply window closed or invalid. Use the staff template workflow.");
    }

    public async Task<AiOutboundIntent> DispatchAsync(Guid id, Guid actor, CancellationToken ct)
    {
        var seed = await db.AiOutboundIntents.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id && i.TenantId == controls.Options.TenantId, ct)
            ?? throw new AiSalesValidation("Dispatch unavailable.");
        // Shared with staff sends and takeover. Once transport starts, takeover waits at most the bounded call;
        // an accepted message cannot be recalled. Provider generation continues to release this lock.
        await using var gate = await ConversationLock.AcquireAsync(db, "conversation:" + seed.ConversationId, ct);
        db.ChangeTracker.Clear();
        var intent = await db.AiOutboundIntents.SingleAsync(i => i.Id == id, ct);
        if (intent.State == "Sending")
        {
            intent.State = "Unknown"; intent.ErrorCode = "InterruptedDispatch"; intent.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct); return intent;
        }
        if (intent.State != "Queued") return intent;
        var chat = await db.WhatsAppConversations.Include(c => c.Contact).Include(c => c.WhatsAppAccount).SingleAsync(c => c.Id == intent.ConversationId, ct);
        RequireRelease(chat.WhatsAppAccountId, chat.Id);
        var draft = await db.AiSalesDrafts.AsNoTracking().SingleAsync(d => d.Id == intent.DraftId && d.TenantId == intent.TenantId, ct);
        if (!await EligibleAsync(draft, ct))
        {
            intent.State = "Invalidated"; intent.ErrorCode = "ContextChanged"; intent.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct); return intent;
        }
        await RequireWindowAsync(chat.Id, ct);
        var now = DateTime.UtcNow;
        var message = new WhatsAppMessage { Id = Guid.NewGuid(), ConversationId = chat.Id, WhatsAppAccountId = chat.WhatsAppAccountId,
            SenderAiSalesAgentId = draft.AgentId, Direction = (byte)MessageDirection.Outgoing, MessageType = (byte)WhatsAppMessageType.Text,
            MessageSource = (byte)MessageSource.System, TextBody = draft.Reply, Status = (byte)MessageStatus.Queued, CreatedAt = now, WhatsAppTimestamp = now };
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await ConversationLock.LockTransactionAsync(db, "dispatch-budget:" + intent.TenantId, ct);
            if (await db.AiOutboundIntents.CountAsync(i => i.TenantId == intent.TenantId && i.AttemptedAt >= now.Date, ct) >= options.Value.MaxAttemptsPerDay)
                throw new AiSalesConflict("Daily dispatch attempt limit reached.");
            intent.State = "Sending"; intent.AttemptedAt = now; intent.MessageId = message.Id;
            db.WhatsAppMessages.Add(message); Audit(intent, actor, "AiDispatchAttempted");
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        WhatsAppSendResult? result = null;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try { result = await transport.SendTextMessageAsync(chat.WhatsAppAccount.PhoneNumberId, chat.Contact.WhatsAppPhoneNumber, draft.Reply, timeout.Token); }
        catch (Exception) { /* The request may have reached Meta. Never retry or expose provider errors. */ }
        // The HTTP request ending or caller disconnecting must not cancel the durable result write.
        var accepted = result?.Success == true && !string.IsNullOrWhiteSpace(result.WhatsAppMessageId) && result.WhatsAppMessageId.Length <= 100;
        intent.State = accepted ? "Sent" : "Unknown"; intent.CompletedAt = DateTime.UtcNow;
        intent.ErrorCode = accepted ? null : "TransportOutcomeUnknown";
        message.Status = accepted ? (byte)MessageStatus.Sent : (byte)MessageStatus.Queued;
        message.WhatsAppMessageId = accepted ? result!.WhatsAppMessageId : null;
        message.ErrorCode = intent.ErrorCode;
        message.ErrorMessage = accepted ? null : "نتيجة الإرسال غير مؤكدة. راجع سجل الإرسال قبل إرسال رد آخر.";
        if (accepted) chat.LastMessageAt = message.CreatedAt;
        Audit(intent, actor, accepted ? "AiDispatchAccepted" : "AiDispatchUnknown");
        await db.SaveChangesAsync(CancellationToken.None);
        try { await notifier.NewMessageAsync(message, chat.AssignedSalesAgentId, "المساعد الافتراضي"); }
        catch (Exception) { /* A notification failure must not repeat the transport. */ }
        return intent;
    }

    public async Task<AiOutboundIntent> CancelAsync(Guid id, Guid actor, CancellationToken ct)
    {
        var seed = await db.AiOutboundIntents.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id && i.TenantId == controls.Options.TenantId, ct)
            ?? throw new AiSalesValidation("Dispatch unavailable.");
        await using var gate = await ConversationLock.AcquireAsync(db, "conversation:" + seed.ConversationId, ct);
        db.ChangeTracker.Clear();
        var intent = await db.AiOutboundIntents.SingleAsync(i => i.Id == id, ct);
        if (intent.State == "Queued")
        {
            intent.State = "Cancelled"; intent.CompletedAt = DateTime.UtcNow; Audit(intent, actor, "AiDispatchCancelled"); await db.SaveChangesAsync(ct);
        }
        else if (intent.State is "Sending" or "Unknown") throw new AiSalesConflict("Transport may have accepted this reply; cancellation cannot confirm non-delivery.");
        return intent;
    }

    private void Audit(AiOutboundIntent i, Guid actor, string action) => db.AiControlAudits.Add(new() { TenantId = i.TenantId,
        ConversationId = i.ConversationId, ActorId = actor, Event = action, OwnershipVersion = i.OwnershipVersion, Reason = "Dispatch record " + i.Id });
}
