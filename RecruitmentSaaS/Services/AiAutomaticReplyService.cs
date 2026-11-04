using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Services;

/// <summary>One durable draft/intent per inbound message and ownership version. Never attributes human review.</summary>
public sealed class AiAutomaticReplyService(RecruitmentCrmContext db, AiSalesControlService controls,
    AiSalesDraftService drafts, AiSalesDispatchService dispatch, IOptions<AiSalesEngineOptions> engine,
    IOptions<AiSalesDispatchOptions> release, IOptions<AiAdminLearningOptions>? adminOptions = null)
{
    public async Task RunConversationAsync(Guid id, CancellationToken ct)
    {
        var settings = engine.Value;
        if (!controls.Options.Enabled || !settings.AutoReplyEnabled || !settings.DraftsEnabled ||
            !release.Value.AutomaticEnabled || !release.Value.Enabled || !release.Value.ReleaseApproved ||
            (!settings.AutoReplyAnyAssignedConversation && (!settings.ApprovedConversationIds.Contains(id) || !release.Value.ApprovedConversationIds.Contains(id)))) return;
        await using var operation = await ConversationLock.AcquireAsync(db, "automatic-reply:" + id, ct);
        var chat = await db.WhatsAppConversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);
        if (chat?.AssignedAiSalesAgentId == null || !settings.CanReplyAutomatically(id, chat.AssignedAiSalesAgentId.Value) ||
            !release.Value.ApprovedAccountIds.Contains(chat.WhatsAppAccountId)) return;
        var control = await db.ConversationControls.AsNoTracking().SingleOrDefaultAsync(c => c.ConversationId == id && c.TenantId == controls.Options.TenantId, ct);
        if (control?.Controller != ConversationControllerMode.Assisted) return;
        if (settings.AutoReplyAnyAssignedConversation && !await db.AiLeadAssignments.AnyAsync(a =>
            a.TenantId == controls.Options.TenantId && a.ConversationId == id && a.AgentId == chat.AssignedAiSalesAgentId &&
            a.EndedAt == null && a.StartedAt >= settings.AutoReplySinceUtc, ct)) return;
        var incoming = await db.WhatsAppMessages.AsNoTracking().Where(m => m.ConversationId == id && m.Direction == 1)
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).FirstOrDefaultAsync(ct);
        var now = DateTime.UtcNow;
        if (incoming == null || (!settings.AutoReplyAnyAssignedConversation && incoming.CreatedAt < settings.AutoReplySinceUtc) || incoming.CreatedAt > now.AddSeconds(-4) ||
            incoming.WhatsAppTimestamp < now.AddHours(-24).AddMinutes(5)) return;
        if (adminOptions?.Value.PrivateRoutingEnabled == true && await db.AiAdminClarifications.AnyAsync(r => r.TenantId == controls.Options.TenantId &&
            r.ConversationId == id && r.CustomerMessageId == incoming.Id, ct)) return; // This turn requires the reviewed AskAdmin path.
        // Ignore delivery/read receipts and do not answer a turn already followed by a staff or AI message.
        if (await db.WhatsAppMessages.AnyAsync(m => m.ConversationId == id && m.Direction == 2 && m.CreatedAt >= incoming.CreatedAt, ct)) return;
        if (await db.AiOutboundIntents.AnyAsync(i => i.ConversationId == id && (i.State == "Sending" || i.State == "Unknown"), ct)) return;
        var key = $"auto:{id}:{incoming.Id}:{control.OwnershipVersion}";
        var draft = await drafts.GenerateAutomaticallyAsync(id, control.OwnershipVersion, key, ct);
        if (draft.NeedsHuman || draft.Status is not (AiDraftStatus.ReviewRequired or AiDraftStatus.AutomaticallyApproved)) return;
        await drafts.ApproveAutomaticallyAsync(draft.Id, ct);
        var intent = await dispatch.QueueAutomaticallyAsync(draft.Id, control.OwnershipVersion, key, ct);
        await dispatch.DispatchAutomaticallyAsync(intent.Id, ct);
    }
}

public sealed class AiAutomaticReplyWorker(IServiceScopeFactory scopes, ILogger<AiAutomaticReplyWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var settings = scope.ServiceProvider.GetRequiredService<IOptions<AiSalesEngineOptions>>().Value;
                if (settings.AutoReplyEnabled)
                {
                    var db = scope.ServiceProvider.GetRequiredService<RecruitmentCrmContext>();
                    var tenant = scope.ServiceProvider.GetRequiredService<AiSalesControlService>().Options.TenantId;
                    var ids = settings.AutoReplyAnyAssignedConversation
                        ? await CandidatesAsync(db, settings, tenant, ct)
                        : settings.ApprovedConversationIds.Distinct().Take(50).ToArray();
                    foreach (var id in ids)
                    {
                        try
                        {
                            using var turn = scopes.CreateScope();
                            await turn.ServiceProvider.GetRequiredService<AiAutomaticReplyService>().RunConversationAsync(id, ct);
                        }
                        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                        catch (AiSalesConflict) { /* Context changed or quota reached; never bypass the guard. */ }
                        catch (AiSalesValidation) { /* Disabled assignment or reply window. */ }
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError("Automatic reply worker failed ({ErrorType})", ex.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(4), ct);
        }
    }

    /// <summary>
    /// Assigned conversations whose latest customer turn still needs an automatic attempt. Turns that already have an
    /// automatic draft (handoff, AskAdmin, failed or stale) are skipped so they cannot occupy the batch for 24 hours.
    /// The key format must match <see cref="AiAutomaticReplyService.RunConversationAsync"/>.
    /// </summary>
    public static async Task<Guid[]> CandidatesAsync(RecruitmentCrmContext db, AiSalesEngineOptions settings, Guid tenant, CancellationToken ct)
    {
        var windowStart = DateTime.UtcNow.AddHours(-24).AddMinutes(5);
        var candidates = await db.AiLeadAssignments.AsNoTracking().Where(a => a.TenantId == tenant && a.EndedAt == null &&
                a.StartedAt >= settings.AutoReplySinceUtc && settings.AutoReplyAgentIds.Contains(a.AgentId) &&
                db.WhatsAppMessages.Any(m => m.ConversationId == a.ConversationId && m.Direction == 1 &&
                    m.WhatsAppTimestamp >= windowStart &&
                    !db.WhatsAppMessages.Any(o => o.ConversationId == a.ConversationId && o.Direction == 2 && o.CreatedAt >= m.CreatedAt)) &&
                !db.AiOutboundIntents.Any(i => i.ConversationId == a.ConversationId && (i.State == "Sending" || i.State == "Unknown")))
            .OrderBy(a => a.StartedAt)
            .Select(a => new {
                a.ConversationId,
                Latest = db.WhatsAppMessages.Where(m => m.ConversationId == a.ConversationId && m.Direction == 1)
                    .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Select(m => m.Id).FirstOrDefault(),
                Version = db.ConversationControls.Where(c => c.TenantId == tenant && c.ConversationId == a.ConversationId)
                    .Select(c => (long?)c.OwnershipVersion).FirstOrDefault() })
            .Take(1000).ToArrayAsync(ct);
        string Key(Guid chat, Guid latest, long? version) => $"auto:{chat}:{latest}:{version}";
        var keys = candidates.Select(c => Key(c.ConversationId, c.Latest, c.Version)).ToArray();
        var handled = (await db.AiSalesDrafts.AsNoTracking().Where(d => d.TenantId == tenant && keys.Contains(d.RequestKey))
            .Select(d => d.RequestKey).ToArrayAsync(ct)).ToHashSet();
        return candidates.Where(c => !handled.Contains(Key(c.ConversationId, c.Latest, c.Version)))
            .Select(c => c.ConversationId).Take(50).ToArray();
    }
}
