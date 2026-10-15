using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RecruitmentSaaS.Services;

public sealed record AiControlResult(Guid ConversationId, long OwnershipVersion);

public sealed class AiSalesControlService(RecruitmentCrmContext db, IOptions<AiSalesOptions> options)
{
    public AiSalesOptions Options => options.Value;
    public void RequireEnabled()
    {
        if (!Options.Enabled || Options.TenantId == Guid.Empty)
            throw new AiSalesValidation("AI Sales is disabled or its deployment tenant is not configured.");
    }

    public async Task<AiControlResult> AssignAsync(Guid leadId, Guid conversationId, Guid agentId,
        Guid? actorId, long expectedVersion, string reason, string requestKey, CancellationToken ct,
        bool conversationOnly = false, string? sourceReference = null)
    {
        RequireEnabled(); ValidateRequest(reason, requestKey);
        if (actorId == null && string.IsNullOrWhiteSpace(sourceReference)) throw new AiSalesValidation("System assignment requires an approval source.");
        if (sourceReference?.Length > 500) throw new AiSalesValidation("Approval source exceeds its limit.");
        var fingerprint = Fingerprint(new { action = "assign", leadId, conversationId, agentId, actorId, expectedVersion, reason, conversationOnly, sourceReference });
        await using var requestLock = await ConversationLock.AcquireAsync(db, "request:" + Options.TenantId + ":" + requestKey, ct);
        var replay = await ReplayAsync(requestKey, fingerprint, ct); if (replay != null) return replay;
        await using var agentLock = await ConversationLock.AcquireAsync(db, "agent:" + agentId, ct);
        await using var leadLock = await ConversationLock.AcquireAsync(db, "ai-lead:" + leadId, ct);
        await using var chatLock = await ConversationLock.AcquireAsync(db, "conversation:" + conversationId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var agent = await db.AiSalesAgents.SingleOrDefaultAsync(a => a.Id == agentId && a.TenantId == Options.TenantId, ct)
            ?? throw new AiSalesValidation("Agent unavailable.");
        if (agent.Mode != AiSalesMode.Shadow) throw new AiSalesValidation("Select a shadow-mode agent first.");
        var lead = await db.Leads.SingleOrDefaultAsync(l => l.Id == leadId, ct) ?? throw new AiSalesValidation("Lead unavailable.");
        var chat = await db.WhatsAppConversations.Include(c => c.WhatsAppAccount).SingleOrDefaultAsync(c => c.Id == conversationId, ct)
            ?? throw new AiSalesValidation("Conversation unavailable.");
        if (chat.LeadId != leadId || lead.IsConverted || lead.IsDuplicate || lead.Status is 6 or 7 or 8 or 9)
            throw new AiSalesValidation("Select an eligible, linked lead and conversation.");
        if (chat.WhatsAppAccountId != agent.WhatsAppAccountId || !chat.WhatsAppAccount.IsActive)
            throw new AiSalesValidation("Agent and conversation account must match and be active.");
        var team = lead.TeamManagerId;
        if (team == null && lead.AssignedSalesId != null)
            team = await db.Users.Where(u => u.Id == lead.AssignedSalesId).Select(u => u.ManagerId).SingleAsync(ct);
        var accountTeam = await WhatsAppScope.TeamOfAccountAsync(db, chat.WhatsAppAccountId, ct);
        if ((!conversationOnly && team != agent.TeamManagerId) || accountTeam != agent.TeamManagerId)
            throw new AiSalesValidation("Lead, account and agent must belong to the same team scope.");
        if (lead.AssignedAiSalesAgentId != null || chat.AssignedAiSalesAgentId != null ||
            await db.AiLeadAssignments.AnyAsync(a => a.LeadId == leadId && a.EndedAt == null && (!conversationOnly || !a.ConversationOnly), ct))
            throw new AiSalesConflict("Already AI assigned; take over or release the current assignment first.");
        if (!conversationOnly && await db.WhatsAppConversations.AnyAsync(c => c.LeadId == leadId && c.Id != conversationId, ct))
            throw new AiSalesValidation("Multiple conversations require human review before this pilot assignment.");
        if (await db.AiLeadAssignments.CountAsync(a => a.AgentId == agentId && a.EndedAt == null, ct) >= agent.MaxActiveConversations)
            throw new AiSalesConflict("Agent active assignment limit reached.");
        var control = await GetControlAsync(chat, ct);
        CheckVersion(control, expectedVersion);
        db.AiLeadAssignments.Add(new()
        {
            TenantId = Options.TenantId, LeadId = lead.Id, ConversationId = chat.Id, AgentId = agent.Id,
            AssignedById = actorId, PreviousSalesId = lead.AssignedSalesId,
            PreviousConversationSalesId = chat.AssignedSalesAgentId, Reason = reason.Trim(),
            ConversationOnly = conversationOnly, SourceReference = sourceReference
        });
        if (!conversationOnly) { lead.AssignedAiSalesAgentId = agent.Id; lead.AssignedSalesId = null; lead.UpdatedAt = DateTime.UtcNow; }
        chat.AssignedAiSalesAgentId = agent.Id; chat.AssignedSalesAgentId = null;
        chat.PendingTeamManagerId = agent.TeamManagerId;
        chat.UpdatedAt = DateTime.UtcNow;
        Change(control, agent.Paused ? ConversationControllerMode.Paused :
            chat.IntakeStatus == (byte)IntakeStatus.Collecting ? ConversationControllerMode.Intake : ConversationControllerMode.Assisted,
            actorId, "AssignedShadow", reason);
        // Seed known intake fields only from actual lead values. Do not reset the intake state.
        chat.IntakeName ??= string.IsNullOrWhiteSpace(lead.FullName) ? null : lead.FullName;
        chat.IntakeAge ??= lead.Age;
        chat.IntakeJob ??= lead.InterestedJobTitle;
        var result = RecordRequest(requestKey, fingerprint, control);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return result;
    }

    public async Task<AiControlResult> ControlAsync(Guid conversationId, Guid? actorId, long expectedVersion,
        string action, string reason, string requestKey, CancellationToken ct, string? sourceReference = null)
    {
        ValidateRequest(reason, requestKey);
        if (action is not ("takeover" or "resume" or "release")) throw new AiSalesValidation("Unknown control action.");
        if (action == "resume") RequireEnabled();
        if (actorId == null && string.IsNullOrWhiteSpace(sourceReference)) throw new AiSalesValidation("System control requires an approval source.");
        if (sourceReference?.Length > 500) throw new AiSalesValidation("Approval source exceeds its limit.");
        var fingerprint = Fingerprint(new { action, conversationId, actorId, expectedVersion, reason, sourceReference });
        await using var requestLock = await ConversationLock.AcquireAsync(db, "request:" + Options.TenantId + ":" + requestKey, ct);
        var replay = await ReplayAsync(requestKey, fingerprint, ct); if (replay != null) return replay;
        await using var chatLock = await ConversationLock.AcquireAsync(db, "conversation:" + conversationId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var chat = await db.WhatsAppConversations.SingleOrDefaultAsync(c => c.Id == conversationId, ct)
            ?? throw new AiSalesValidation("Conversation unavailable.");
        var control = await db.ConversationControls.SingleOrDefaultAsync(c => c.ConversationId == conversationId && c.TenantId == Options.TenantId, ct)
            ?? throw new AiSalesValidation("No AI control record.");
        CheckVersion(control, expectedVersion);
        if (action == "resume")
        {
            var agent = await db.AiSalesAgents.SingleOrDefaultAsync(a => a.Id == chat.AssignedAiSalesAgentId && a.TenantId == Options.TenantId, ct);
            if (agent == null || agent.Paused || agent.Mode != AiSalesMode.Shadow)
                throw new AiSalesValidation("The assigned agent is unavailable for shadow operation.");
            Change(control, ConversationControllerMode.Assisted, actorId, "ResumedShadow", reason);
        }
        else
        {
            await TakeOverLockedAsync(chat, actorId, reason, ct);
            if (action == "release")
            {
                var assignment = await db.AiLeadAssignments.SingleOrDefaultAsync(a => a.ConversationId == chat.Id && a.EndedAt == null, ct);
                if (assignment == null) throw new AiSalesValidation("No active assignment.");
                // Explicit admin release restores an active former owner, otherwise leaves the scoped review queue.
                var agent = await db.AiSalesAgents.SingleAsync(a => a.Id == assignment.AgentId, ct);
                var previousLead = await ActiveUserAsync(assignment.PreviousSalesId, agent.TeamManagerId, ct);
                var previousChat = assignment.ConversationOnly
                    ? await db.Users.Where(u => u.Id == assignment.PreviousConversationSalesId && u.IsActive).Select(u => (Guid?)u.Id).SingleOrDefaultAsync(ct)
                    : await ActiveUserAsync(assignment.PreviousConversationSalesId, agent.TeamManagerId, ct);
                var lead = await db.Leads.SingleAsync(l => l.Id == assignment.LeadId, ct);
                if (!assignment.ConversationOnly) { lead.AssignedAiSalesAgentId = null; lead.AssignedSalesId = previousLead; }
                chat.AssignedAiSalesAgentId = null; chat.AssignedSalesAgentId = previousChat;
                assignment.EndedAt = DateTime.UtcNow;
                if (previousChat != null) chat.PendingTeamManagerId = null;
                Change(control, ConversationControllerMode.Human, actorId, "ReleasedToHumanWorkflow", reason);
            }
        }
        var result = RecordRequest(requestKey, fingerprint, control);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return result;
    }

    // Caller holds the conversation lock. Also used for ordinary staff sends and mobile-app echoes.
    public async Task TakeOverLockedAsync(WhatsAppConversation chat, Guid? actorId, string reason, CancellationToken ct)
    {
        var control = await db.ConversationControls.SingleOrDefaultAsync(c => c.ConversationId == chat.Id, ct);
        if (control != null)
        {
            var lead = chat.LeadId == null ? null : await db.Leads.FindAsync(new object[] { chat.LeadId.Value }, ct);
            control.HandoffSummary = $"Name: {chat.IntakeName ?? lead?.FullName ?? "unknown"}; Age: {chat.IntakeAge ?? lead?.Age}; Profession: {chat.IntakeJob ?? lead?.InterestedJobTitle ?? "unknown"}; Lead status: {lead?.Status}; Appointment: {lead?.AppointmentDate:yyyy-MM-dd HH:mm}. Verified vacancy: not established by Phase 1.";
            if (control.HandoffSummary.Length > 2000) control.HandoffSummary = control.HandoffSummary[..2000];
            Change(control, ConversationControllerMode.Human, actorId, "HumanTakeover", reason);
            await db.AiOutboundIntents.Where(i => i.ConversationId == chat.Id && (i.State == "Blocked" || i.State == "Queued"))
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.State, "Invalidated"), ct);
        }
        if (chat.IntakeStatus == (byte)IntakeStatus.Collecting) chat.IntakeStatus = (byte)IntakeStatus.StoppedByStaff;
    }

    public async Task CompleteIntakeLockedAsync(WhatsAppConversation chat, CancellationToken ct)
    {
        var control = await db.ConversationControls.SingleOrDefaultAsync(c => c.ConversationId == chat.Id, ct);
        if (control?.Controller == ConversationControllerMode.Intake)
            Change(control, ConversationControllerMode.Assisted, null, "IntakeCompleted", "Existing intake data retained; shadow only.");
    }

    public async Task PauseAgentConversationsAsync(Guid agentId, Guid actorId, CancellationToken ct)
    {
        // The caller holds the agent lock and transaction. These locks share the same
        // advisory key as session locks and remain held until the settings commit.
        var ids = await db.WhatsAppConversations.Where(c => c.AssignedAiSalesAgentId == agentId).OrderBy(c => c.Id).Select(c => c.Id).ToListAsync(ct);
        foreach (var id in ids)
        {
            await ConversationLock.LockTransactionAsync(db, "conversation:" + id, ct);
            var control = await db.ConversationControls.SingleOrDefaultAsync(c => c.ConversationId == id && c.TenantId == Options.TenantId, ct);
            if (control == null || control.Controller == ConversationControllerMode.Human) continue;
            Change(control, ConversationControllerMode.Paused, actorId, "AgentPaused", "Administrator paused or disabled the agent.");
            var chat = await db.WhatsAppConversations.SingleAsync(c => c.Id == id, ct);
            if (chat.IntakeStatus == (byte)IntakeStatus.Collecting) chat.IntakeStatus = (byte)IntakeStatus.StoppedByStaff;
            await db.AiOutboundIntents.Where(i => i.ConversationId == id && (i.State == "Blocked" || i.State == "Queued"))
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.State, "Invalidated"), ct);
        }
    }

    private async Task<Guid?> ActiveUserAsync(Guid? id, Guid? teamId, CancellationToken ct) => id != null &&
        await db.Users.AnyAsync(u => u.Id == id && u.IsActive && (u.Role == 3 || u.Role == 8)
            && u.ManagerId == teamId, ct) ? id : null;
    private async Task<ConversationControl> GetControlAsync(WhatsAppConversation chat, CancellationToken ct)
    {
        var control = await db.ConversationControls.SingleOrDefaultAsync(c => c.ConversationId == chat.Id, ct);
        if (control != null)
        {
            if (control.TenantId != Options.TenantId) throw new AiSalesValidation("Invalid deployment scope.");
            return control;
        }
        control = new() { ConversationId = chat.Id, TenantId = Options.TenantId, OwnershipVersion = 0 };
        db.ConversationControls.Add(control); return control;
    }
    private void Change(ConversationControl control, ConversationControllerMode controller, Guid? actorId, string action, string reason)
    {
        control.Controller = controller; control.OwnershipVersion++; control.LastActorId = actorId;
        control.Reason = reason.Trim(); control.UpdatedAt = DateTime.UtcNow;
        db.AiControlAudits.Add(new() { TenantId = control.TenantId, ConversationId = control.ConversationId,
            ActorId = actorId, Event = action, Reason = control.Reason, OwnershipVersion = control.OwnershipVersion });
    }
    private static void CheckVersion(ConversationControl control, long expected)
    {
        if (expected < 0 || control.OwnershipVersion != expected) throw new AiSalesConflict("Control changed. Refresh before retrying.");
    }
    private static void ValidateRequest(string reason, string key)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500) throw new AiSalesValidation("Reason is required (maximum 500 characters).");
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128) throw new AiSalesValidation("Idempotency-Key is required (maximum 128 characters).");
    }
    private static string Fingerprint(object request) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
    private async Task<AiControlResult?> ReplayAsync(string key, string fingerprint, CancellationToken ct)
    {
        var previous = await db.AiControlRequests.FindAsync(new object[] { Options.TenantId, key }, ct);
        if (previous == null) return null;
        if (previous.Fingerprint != fingerprint) throw new AiSalesConflict("Idempotency key was already used for another request.");
        return new(previous.ResourceId, previous.Version);
    }
    private AiControlResult RecordRequest(string key, string fingerprint, ConversationControl control)
    {
        db.AiControlRequests.Add(new() { TenantId = Options.TenantId, Key = key, Fingerprint = fingerprint,
            ResourceId = control.ConversationId, Version = control.OwnershipVersion });
        return new(control.ConversationId, control.OwnershipVersion);
    }
}
