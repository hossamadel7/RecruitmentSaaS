using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RecruitmentSaaS.Controllers.Api;

[ApiController, Route("api/ai-sales"), Authorize(Roles = "1,7,8"), AutoValidateAntiforgeryToken]
public sealed class AiSalesController(RecruitmentCrmContext db, AiSalesControlService controls) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("setup"), Authorize(Roles = "1")]
    public async Task<IActionResult> Setup(CancellationToken ct)
    {
        var accounts = await db.WhatsAppAccounts.AsNoTracking().Where(a => a.IsActive).OrderBy(a => a.Name)
            .Select(a => new { a.Id, a.Name }).ToListAsync(ct);
        var result = new List<object>();
        foreach (var account in accounts)
            result.Add(new { account.Id, account.Name, teamManagerId = await WhatsAppScope.TeamOfAccountAsync(db, account.Id, ct) });
        return Ok(result);
    }

    [HttpGet("candidates"), Authorize(Roles = "1")]
    public async Task<IActionResult> Candidates([FromQuery] string? search, CancellationToken ct, [FromQuery] Guid? accountId = null)
    {
        var query = db.WhatsAppConversations.AsNoTracking().Where(c => c.LeadId != null);
        if (accountId != null) query = query.Where(c => c.WhatsAppAccountId == accountId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            if (search.Length > 100) return BadRequest();
            query = query.Where(c => c.Lead!.FullName.Contains(search) || c.Lead.Phone.Contains(search) || c.Lead.LeadCode == search);
        }
        return Ok(await query.OrderByDescending(c => c.LastMessageAt).Take(50).Select(c => new
        {
            conversationId = c.Id, c.LeadId, name = c.Lead!.FullName, c.Lead.LeadCode, c.AssignedAiSalesAgentId,
            c.WhatsAppAccountId, accountName = c.WhatsAppAccount.Name,
            version = db.ConversationControls.Where(x => x.ConversationId == c.Id).Select(x => (long?)x.OwnershipVersion).FirstOrDefault() ?? 0
        }).ToListAsync(ct));
    }
    public sealed record AgentRequest([Required, MaxLength(100)] string DisplayName, Guid WhatsAppAccountId,
        Guid? TeamManagerId, AiSalesMode Mode, bool Paused, [Range(1,1000)] int MaxActiveConversations, long ExpectedVersion);
    public sealed record AssignRequest(Guid AgentId, Guid ConversationId, long ExpectedVersion, [Required, MaxLength(500)] string Reason, bool ConversationOnly = false);
    public sealed record ControlRequest(long ExpectedVersion, [Required, MaxLength(500)] string Reason);

    [HttpGet("agents"), Authorize(Roles = "1")]
    public async Task<IActionResult> Agents(CancellationToken ct) => Ok(new
    {
        enabled = controls.Options.Enabled, autonomousSendingAvailable = true, rotationAvailable = false,
        agents = await db.AiSalesAgents.AsNoTracking().Where(a => a.TenantId == controls.Options.TenantId).OrderBy(a => a.DisplayName).ToListAsync(ct),
        deadLetters = await db.DurableWorkItems.CountAsync(w => w.CompletedAt == null && w.Attempts >= 8, ct)
    });

    [HttpPost("agents"), Authorize(Roles = "1")]
    public Task<IActionResult> CreateAgent(AgentRequest request, CancellationToken ct) => Run(async () =>
    {
        var (key, fingerprint) = AgentRequestIdentity("create", request);
        await using var requestLock = await ConversationLock.AcquireAsync(db, "request:" + controls.Options.TenantId + ":" + key, ct);
        var replay = await ReplayAgent(key, fingerprint, ct); if (replay != null) return Ok(replay);
        controls.RequireEnabled(); await ValidateAgent(request, ct);
        var agent = new AiSalesAgent { TenantId = controls.Options.TenantId, DisplayName = request.DisplayName.Trim(),
            WhatsAppAccountId = request.WhatsAppAccountId, TeamManagerId = request.TeamManagerId,
            Mode = request.Mode, Paused = request.Paused, MaxActiveConversations = request.MaxActiveConversations };
        db.AiSalesAgents.Add(agent); AuditAgent(agent, "AiAgentCreated");
        RememberAgentRequest(key, fingerprint, agent); await db.SaveChangesAsync(ct);
        return Ok(agent);
    });

    [HttpPatch("agents/{id:guid}"), Authorize(Roles = "1")]
    public Task<IActionResult> UpdateAgent(Guid id, AgentRequest request, CancellationToken ct) => Run(async () =>
    {
        var (key, fingerprint) = AgentRequestIdentity("update:" + id, request);
        await using var requestLock = await ConversationLock.AcquireAsync(db, "request:" + controls.Options.TenantId + ":" + key, ct);
        var replay = await ReplayAgent(key, fingerprint, ct); if (replay != null) return Ok(replay);
        await using var gate = await ConversationLock.AcquireAsync(db, "agent:" + id, ct);
        var agent = await db.AiSalesAgents.SingleOrDefaultAsync(a => a.Id == id && a.TenantId == controls.Options.TenantId, ct);
        if (agent == null) return NotFound();
        await ValidateAgent(request, ct);
        if (request.ExpectedVersion != agent.Version) throw new AiSalesConflict("Agent changed; refresh first.");
        if (agent.WhatsAppAccountId != request.WhatsAppAccountId || agent.TeamManagerId != request.TeamManagerId)
            throw new AiSalesValidation("An agent's account and team scope are immutable; create a separate agent.");
        agent.DisplayName = request.DisplayName.Trim(); agent.Paused = request.Paused;
        agent.Mode = request.Mode; agent.MaxActiveConversations = request.MaxActiveConversations; agent.Version++;
        AuditAgent(agent, "AiAgentUpdated");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Pause is persisted with the controls; resume still requires an explicit per-chat action.
        if (agent.Paused || agent.Mode == AiSalesMode.Disabled)
            await controls.PauseAgentConversationsAsync(agent.Id, Actor, ct);
        RememberAgentRequest(key, fingerprint, agent);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Ok(agent);
    });

    [HttpPost("leads/{id:guid}/assign"), Authorize(Roles = "1")]
    public Task<IActionResult> Assign(Guid id, AssignRequest request, CancellationToken ct) => Run(async () =>
        Ok(await controls.AssignAsync(id, request.ConversationId, request.AgentId, Actor, request.ExpectedVersion,
            request.Reason, Request.Headers["Idempotency-Key"].ToString(), ct, request.ConversationOnly)));

    [HttpGet("conversations/{id:guid}")]
    public async Task<IActionResult> Conversation(Guid id, CancellationToken ct)
    {
        if (!await CanSee(id, ct)) return NotFound();
        var chat = await db.WhatsAppConversations.AsNoTracking().SingleAsync(c => c.Id == id, ct);
        var control = await db.ConversationControls.AsNoTracking().SingleOrDefaultAsync(c => c.ConversationId == id && c.TenantId == controls.Options.TenantId, ct);
        return Ok(new { chat.Id, chat.LeadId, chat.WhatsAppAccountId, chat.AssignedAiSalesAgentId, control,
            audit = await db.AiControlAudits.AsNoTracking().Where(a => a.ConversationId == id && a.TenantId == controls.Options.TenantId)
                .OrderByDescending(a => a.CreatedAt).Take(30).ToListAsync(ct) });
    }

    [HttpPost("conversations/{id:guid}/{operation:regex(^(takeover|resume|release)$)}")]
    public Task<IActionResult> Control(Guid id, string operation, ControlRequest request, CancellationToken ct) => Run(async () =>
    {
        if (!User.IsInRole("1") && operation != "takeover") return Forbid();
        if (!await CanSee(id, ct)) return NotFound();
        return Ok(await controls.ControlAsync(id, Actor, request.ExpectedVersion, operation, request.Reason,
            Request.Headers["Idempotency-Key"].ToString(), ct));
    });

    private async Task<bool> CanSee(Guid id, CancellationToken ct) => await db.WhatsAppConversations
        .ApplyVisibility(await WhatsAppScope.ForUserAsync(db, User.FindFirstValue(ClaimTypes.Role), Actor, ct)).AnyAsync(c => c.Id == id, ct);
    private (string Key, string Fingerprint) AgentRequestIdentity(string action, AgentRequest request)
    {
        var key = Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128) throw new AiSalesValidation("Idempotency-Key is required (maximum 128 characters).");
        return (key, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { action, Actor, request })))));
    }
    private async Task<AiSalesAgent?> ReplayAgent(string key, string fingerprint, CancellationToken ct)
    {
        var previous = await db.AiControlRequests.FindAsync(new object[] { controls.Options.TenantId, key }, ct);
        if (previous == null) return null;
        if (previous.Fingerprint != fingerprint) throw new AiSalesConflict("Idempotency key already used for another request.");
        return await db.AiSalesAgents.SingleAsync(a => a.Id == previous.ResourceId && a.TenantId == controls.Options.TenantId, ct);
    }
    private void RememberAgentRequest(string key, string fingerprint, AiSalesAgent agent) => db.AiControlRequests.Add(new()
    {
        TenantId = controls.Options.TenantId, Key = key, Fingerprint = fingerprint, ResourceId = agent.Id, Version = agent.Version
    });
    private async Task ValidateAgent(AgentRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.Mode is not (AiSalesMode.Disabled or AiSalesMode.Shadow))
            throw new AiSalesValidation("Only Disabled and Shadow modes are available.");
        if (request.Mode == AiSalesMode.Shadow && !request.Paused) controls.RequireEnabled();
        if (!await db.WhatsAppAccounts.AnyAsync(a => a.Id == request.WhatsAppAccountId && a.IsActive, ct))
            throw new AiSalesValidation("Account unavailable.");
        if (await WhatsAppScope.TeamOfAccountAsync(db, request.WhatsAppAccountId, ct) != request.TeamManagerId)
            throw new AiSalesValidation("Team must match the account's configured team.");
    }
    private void AuditAgent(AiSalesAgent agent, string action) => db.AuditLogs.Add(new()
    {
        Id = Guid.NewGuid(), ActorId = Actor, ActorType = 1, EventType = action, EntityType = "AiSalesAgent",
        EntityId = agent.Id, NewValueJson = System.Text.Json.JsonSerializer.Serialize(new { agent.Mode, agent.Paused, agent.MaxActiveConversations, agent.Version }), CreatedAt = DateTime.UtcNow
    });
    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (AiSalesValidation ex) { return UnprocessableEntity(new { error = ex.Message }); }
        catch (AiSalesConflict ex) { return Conflict(new { error = ex.Message }); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { error = "State changed; refresh before retrying." }); }
    }
}
