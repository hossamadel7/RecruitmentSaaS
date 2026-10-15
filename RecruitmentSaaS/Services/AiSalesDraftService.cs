using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using System.Text.Json;

namespace RecruitmentSaaS.Services;

public sealed class AiSalesDraftService(RecruitmentCrmContext db, AiSalesControlService controls,
    AiSalesContextService contextReader, IAiSalesDraftProvider provider, IOptions<AiSalesEngineOptions> options)
{
    public async Task<AiSalesDraft> GenerateAsync(Guid chatId, Guid actor, long expectedVersion, string key, CancellationToken ct)
    {
        controls.RequireEnabled();
        var settings = options.Value;
        if (!settings.DraftsEnabled) throw new AiSalesValidation("Reply drafts are disabled.");
        if (settings.ApprovedConversationIds.Length > 0 && !settings.ApprovedConversationIds.Contains(chatId))
            throw new AiSalesValidation("This conversation is outside the approved draft pilot.");
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128) throw new AiSalesValidation("Idempotency-Key is required (maximum 128 characters).");
        var tenant = controls.Options.TenantId;
        // Held across this request only. A duplicate cannot invoke the paid provider twice.
        await using var requestGate = await ConversationLock.AcquireAsync(db, "draft-request:" + tenant + ":" + key, ct);
        var previous = await db.AiSalesDrafts.SingleOrDefaultAsync(d => d.TenantId == tenant && d.RequestKey == key, ct);
        if (previous != null)
        {
            if (previous.ConversationId != chatId || previous.RequestedById != actor || previous.OwnershipVersion != expectedVersion)
                throw new AiSalesConflict("Idempotency key already used for another draft request.");
            if (previous.Status == AiDraftStatus.Generating)
            {
                // The request lock is acquired only after any live generator releases it.
                previous.Status = AiDraftStatus.Failed; previous.ErrorCode = "InterruptedGeneration"; previous.CompletedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }
            return previous;
        }
        AiSalesDraft draft; AiSalesContext context;
        await using (var chatGate = await ConversationLock.AcquireAsync(db, "conversation:" + chatId, ct))
        {
            var chat = await RequireChatAsync(chatId, expectedVersion, ct);
            context = await contextReader.ReadAsync(tenant, chat, ct);
            if (context.RecentMessages.LastOrDefault()?.Speaker != "customer")
                throw new AiSalesValidation("A new customer message is required before drafting.");
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await ConversationLock.LockTransactionAsync(db, "draft-budget:" + tenant, ct);
            var day = DateTime.UtcNow.Date;
            if (await db.AiSalesDrafts.CountAsync(d => d.TenantId == tenant && d.CreatedAt >= day, ct) >= settings.MaxDraftsPerDay)
                throw new AiSalesConflict("Daily draft limit reached.");
            if (await db.AiSalesDrafts.CountAsync(d => d.TenantId == tenant && d.Status == AiDraftStatus.Generating && d.CreatedAt > DateTime.UtcNow.AddMinutes(-2), ct) >= 2)
                throw new AiSalesConflict("Two drafts are already being generated; retry later.");
            draft = new() { TenantId = tenant, ConversationId = chatId, AgentId = chat.AssignedAiSalesAgentId!.Value,
                RequestedById = actor, RequestKey = key, OwnershipVersion = expectedVersion, MemoryVersion = context.Customer.MemoryVersion,
                ContextFingerprint = AiSalesContextService.Fingerprint(context),
                PromptVersion = OpenAiSalesDraftProvider.PromptVersion,
                KnowledgeSnapshotJson = JsonSerializer.Serialize(context.Knowledge.Select(k => new { k.Id, k.Version })),
                Status = AiDraftStatus.Generating };
            db.AiSalesDrafts.Add(draft); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }

        AiDraftProposal? proposal = null; string? error = null;
        try
        {
            proposal = AiSalesReplyPolicy.Create(context);
            if (proposal == null && context.RecentMessages.Reverse().TakeWhile(m => m.Speaker == "customer").Any(m => m.Type is not ("Text" or "TranscribedAudio")))
                proposal = new("ممكن توضح طلبك كتابة عشان نقدر نساعدك بدقة؟", "handoff", "Media has not been interpreted; human review required.", true, [], "local-policy");
            else if (proposal == null && !context.Knowledge.Any(k => k.Kind == AiKnowledgeKind.Fact))
                proposal = new("محتاجين نراجع المعلومة مع موظف قبل ما نأكد أي تفاصيل.", "handoff", "No currently approved facts for this account.", true, [], "local-policy");
            else if (proposal == null)
            {
                if (!settings.ExternalProcessingApproved) throw new AiSalesProviderException("ExternalProcessingDisabled");
                proposal = await provider.GenerateAsync(context, ct);
                ValidateProposal(proposal, context);
            }
        }
        catch (Exception ex)
        {
            // Persist a bounded code only; never provider bodies, prompts, keys or customer text in errors.
            error = ex is AiSalesProviderException ? ex.Message : ex is OperationCanceledException ? "GenerationCancelled" : ex.GetType().Name;
        }

        db.ChangeTracker.Clear();
        await using (var chatGate = await ConversationLock.AcquireAsync(db, "conversation:" + chatId, CancellationToken.None))
        {
            draft = await db.AiSalesDrafts.SingleAsync(d => d.Id == draft.Id);
            var current = await IsCurrentAsync(draft, CancellationToken.None);
            draft.CompletedAt = DateTime.UtcNow;
            // A discarded or invalid proposal can still have incurred provider usage.
            if (proposal != null)
            {
                draft.Model = (proposal.Model ?? "")[..Math.Min(proposal.Model?.Length ?? 0, 100)];
                draft.InputTokens = proposal.InputTokens is >= 0 ? proposal.InputTokens : null;
                draft.OutputTokens = proposal.OutputTokens is >= 0 ? proposal.OutputTokens : null;
            }
            if (!current) { draft.Status = AiDraftStatus.Stale; draft.ErrorCode = "ContextChanged"; }
            else if (error != null) { draft.Status = AiDraftStatus.Failed; draft.ErrorCode = error[..Math.Min(error.Length, 100)]; }
            else
            {
                draft.Status = AiDraftStatus.ReviewRequired; draft.Reply = proposal!.Reply; draft.Intent = proposal.Intent;
                draft.ReviewNote = proposal.ReviewNote; draft.NeedsHuman = proposal.NeedsHuman;
                draft.SourceIdsJson = JsonSerializer.Serialize(proposal.SourceIds.Distinct());
            }
            await db.SaveChangesAsync(CancellationToken.None);
        }
        return draft;
    }

    public async Task<AiSalesDraft> ReviewAsync(Guid id, Guid actor, bool accept, CancellationToken ct)
    {
        var draft = await db.AiSalesDrafts.SingleOrDefaultAsync(d => d.Id == id && d.TenantId == controls.Options.TenantId, ct)
            ?? throw new AiSalesValidation("Draft unavailable.");
        await using var gate = await ConversationLock.AcquireAsync(db, "conversation:" + draft.ConversationId, ct);
        await db.Entry(draft).ReloadAsync(ct);
        if (draft.Status is AiDraftStatus.Reviewed or AiDraftStatus.Rejected) return draft;
        if (draft.Status != AiDraftStatus.ReviewRequired) throw new AiSalesConflict("Draft is not available for review.");
        if (accept && !await IsCurrentAsync(draft, ct))
        {
            draft.Status = AiDraftStatus.Stale; draft.ErrorCode = "ContextChanged"; await db.SaveChangesAsync(ct);
            throw new AiSalesConflict("Draft context changed; generate a new draft.");
        }
        draft.Status = accept ? AiDraftStatus.Reviewed : AiDraftStatus.Rejected;
        draft.ReviewedById = actor; draft.ReviewedAt = DateTime.UtcNow;
        db.AuditLogs.Add(new() { Id = Guid.NewGuid(), ActorId = actor, ActorType = 1, EventType = accept ? "AiDraftReviewed" : "AiDraftRejected", EntityType = "AiSalesDraft", EntityId = draft.Id, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(ct); return draft;
    }

    public async Task<bool> IsCurrentAsync(AiSalesDraft draft, CancellationToken ct)
    {
        var chat = await db.WhatsAppConversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == draft.ConversationId, ct);
        var control = await db.ConversationControls.AsNoTracking().SingleOrDefaultAsync(c => c.ConversationId == draft.ConversationId && c.TenantId == draft.TenantId, ct);
        if (!controls.Options.Enabled || !options.Value.DraftsEnabled || chat?.AssignedAiSalesAgentId != draft.AgentId ||
            control?.OwnershipVersion != draft.OwnershipVersion || control.Controller != ConversationControllerMode.Assisted) return false;
        if (!await db.AiSalesAgents.AnyAsync(a => a.Id == draft.AgentId && a.TenantId == draft.TenantId && !a.Paused && a.Mode == AiSalesMode.Shadow
            && a.WhatsAppAccountId == chat!.WhatsAppAccountId && a.TeamManagerId == chat.PendingTeamManagerId
            && db.WhatsAppAccounts.Any(w => w.Id == a.WhatsAppAccountId && w.IsActive), ct)) return false;
        var context = await contextReader.ReadAsync(draft.TenantId, chat!, ct);
        return AiSalesContextService.Fingerprint(context) == draft.ContextFingerprint;
    }

    private async Task<WhatsAppConversation> RequireChatAsync(Guid id, long version, CancellationToken ct)
    {
        var tenant = controls.Options.TenantId;
        var chat = await db.WhatsAppConversations.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new AiSalesValidation("Conversation unavailable.");
        var control = await db.ConversationControls.AsNoTracking().SingleOrDefaultAsync(c => c.ConversationId == id && c.TenantId == tenant, ct);
        if (control == null || control.OwnershipVersion != version) throw new AiSalesConflict("Control changed; refresh first.");
        if (control.Controller != ConversationControllerMode.Assisted || chat.AssignedAiSalesAgentId == null)
            throw new AiSalesValidation("Drafts require an explicitly assigned, resumed shadow conversation after intake.");
        if (!await db.AiSalesAgents.AnyAsync(a => a.Id == chat.AssignedAiSalesAgentId && a.TenantId == tenant && !a.Paused && a.Mode == AiSalesMode.Shadow
            && a.WhatsAppAccountId == chat.WhatsAppAccountId && a.TeamManagerId == chat.PendingTeamManagerId
            && db.WhatsAppAccounts.Any(w => w.Id == a.WhatsAppAccountId && w.IsActive), ct))
            throw new AiSalesValidation("Assigned agent is unavailable.");
        return chat;
    }

    private static void ValidateProposal(AiDraftProposal p, AiSalesContext context)
    {
        if (string.IsNullOrWhiteSpace(p.Reply) || p.Reply.Length > 3000 || p.ReviewNote == null || p.ReviewNote.Length > 500 || p.Model.Length > 100 ||
            p.Intent is not ("answer" or "qualification" or "consultation" or "handoff" or "stop") || p.SourceIds.Length > 6)
            throw new AiSalesProviderException("InvalidDraft");
        var facts = context.Knowledge.Where(k => k.Kind == AiKnowledgeKind.Fact).Select(k => k.Id).ToHashSet();
        if (p.SourceIds.Any(id => !facts.Contains(id))) throw new AiSalesProviderException("UnapprovedCitation");
        if ((p.Intent is "answer" or "consultation") && p.SourceIds.Length == 0 && !p.NeedsHuman)
            throw new AiSalesProviderException("MissingEvidence");
        if ((p.Intent is "handoff" or "stop") && !p.NeedsHuman) throw new AiSalesProviderException("InvalidHandoff");
        if (AiSalesContextService.Redact(p.Reply) != p.Reply) throw new AiSalesProviderException("SensitiveOutput");
    }
}
