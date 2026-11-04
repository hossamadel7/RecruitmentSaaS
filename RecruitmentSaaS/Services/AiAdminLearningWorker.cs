using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
namespace RecruitmentSaaS.Services;

public sealed class AiAdminLearningWorker(IServiceScopeFactory scopes, ILogger<AiAdminLearningWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var admin = scope.ServiceProvider.GetRequiredService<AiAdminLearningService>();
                if (admin.Options.Enabled && admin.Options.PrivateRoutingEnabled && admin.Options.BackgroundEnabled)
                {
                    var db = scope.ServiceProvider.GetRequiredService<RecruitmentCrmContext>();
                    var channels = await db.AiAdminChannels.AsNoTracking().Where(c => c.TenantId == admin.Tenant &&
                        admin.Options.ApprovedAccountIds.Contains(c.WhatsAppAccountId) && c.VerifiedAt != null && !c.Paused).Select(c => c.Id).ToArrayAsync(ct);
                    foreach (var channel in channels) await admin.QueueQuestionsAsync(channel, ct);
                    var messages = await db.AiAdminMessages.AsNoTracking().Where(m => channels.Contains(m.ChannelId) && !m.Incoming &&
                        (m.State == "Queued" || m.State == "Sending")).OrderBy(m => m.CreatedAt).Select(m => m.Id).Take(10).ToArrayAsync(ct);
                    foreach (var message in messages) await admin.DeliverAsync(message, ct);
                    var requests = await db.AiAdminClarifications.AsNoTracking().Where(r => r.TenantId == admin.Tenant && channels.Contains(r.ChannelId) &&
                        (r.State == AiAdminRequestState.Validated || (r.Deadline < DateTime.UtcNow &&
                        (r.State == AiAdminRequestState.Queued || r.State == AiAdminRequestState.AwaitingAdmin || r.State == AiAdminRequestState.AnswerReceived))))
                        .OrderBy(r => r.CreatedAt).Select(r => r.Id).Take(10).ToArrayAsync(ct);
                    foreach (var id in requests)
                    {
                        using var turn = scopes.CreateScope();
                        try { await turn.ServiceProvider.GetRequiredService<AiAdminDraftResumer>().PrepareAsync(id, ct); }
                        catch (AiSalesValidation) { }
                        catch (AiSalesConflict) { }
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError("Admin coaching worker failed ({ErrorType})", ex.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(15), ct);
        }
    }
}

/// <summary>Resumes preparation only, never dispatch. Context freshness is checked again by existing review/sending.</summary>
public sealed class AiAdminDraftResumer(RecruitmentCrmContext db, AiAdminLearningService admin, AiSalesDraftService drafts)
{
    public async Task<AiAdminClarification> PrepareAsync(Guid id, CancellationToken ct)
    {
        await using var gate = await ConversationLock.AcquireAsync(db, "admin-resume:" + id, ct);
        var r = await db.AiAdminClarifications.SingleAsync(r => r.Id == id && r.TenantId == admin.Tenant, ct);
        var channel = await db.AiAdminChannels.SingleAsync(c => c.Id == r.ChannelId, ct);
        admin.Require(channel.WhatsAppAccountId);
        if (channel.Paused) return r;
        await admin.RequireAdminAsync(channel.AdminUserId, ct);
        if (r.State is AiAdminRequestState.Queued or AiAdminRequestState.AwaitingAdmin or AiAdminRequestState.AnswerReceived)
        {
            if (r.Deadline < DateTime.UtcNow) { admin.Change(r, AiAdminRequestState.Escalated, null); r.ErrorCode = "AdminDeadlineExceeded"; await db.SaveChangesAsync(ct); }
            return r;
        }
        if (r.State != AiAdminRequestState.Validated) return r;
        var chat = await db.WhatsAppConversations.AsNoTracking().SingleAsync(c => c.Id == r.ConversationId, ct);
        var control = await db.ConversationControls.AsNoTracking().SingleOrDefaultAsync(c => c.ConversationId == r.ConversationId && c.TenantId == r.TenantId, ct);
        var newest = await db.WhatsAppMessages.AsNoTracking().Where(m => m.ConversationId == r.ConversationId)
            .OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Select(m => m.Id).FirstOrDefaultAsync(ct);
        if (chat.AssignedAiSalesAgentId != r.AgentId || control?.Controller != ConversationControllerMode.Assisted ||
            control.OwnershipVersion != r.OwnershipVersion || newest != r.CustomerMessageId || r.AnswerValidUntil <= DateTime.UtcNow)
        {
            admin.Change(r, AiAdminRequestState.Cancelled, null); r.ErrorCode = "CustomerContextChanged"; await db.SaveChangesAsync(ct); return r;
        }
        // The actor authorized use of this answer, not approval of the resulting customer wording.
        var draft = await drafts.GenerateAsync(r.ConversationId, r.ValidatedById!.Value, r.OwnershipVersion, "admin-answer:" + r.Id + ":" + r.Version, ct);
        await db.Entry(r).ReloadAsync(ct);
        if (r.State != AiAdminRequestState.Validated) return r;
        r.ReplyDraftId = draft.Id;
        if (draft.Status == AiDraftStatus.ReviewRequired) { admin.Change(r, AiAdminRequestState.ReviewReady, null); r.ErrorCode = null; }
        else { admin.Change(r, AiAdminRequestState.Escalated, null); r.ErrorCode = draft.ErrorCode ?? "DraftNeedsReview"; }
        await db.SaveChangesAsync(ct); return r;
    }
}
