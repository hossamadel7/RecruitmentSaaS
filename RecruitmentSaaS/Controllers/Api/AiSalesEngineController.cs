using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;

namespace RecruitmentSaaS.Controllers.Api;

[ApiController, Route("api/ai-sales-engine"), Authorize(Roles = "1,7,8"), AutoValidateAntiforgeryToken]
public sealed class AiSalesEngineController(RecruitmentCrmContext db, AiSalesControlService controls,
    AiSalesDraftService drafts, IOptions<AiSalesEngineOptions> options, AiSalesImprovementService improvements) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private Guid Tenant => controls.Options.TenantId;
    public sealed record KnowledgeRequest(Guid WhatsAppAccountId, AiKnowledgeKind Kind,
        [Required, MaxLength(150)] string Title, [Required, MaxLength(4000)] string Content,
        [Required, MaxLength(500)] string SourceReference, DateTime ValidFrom, DateTime ValidUntil);
    public sealed record VersionRequest(long ExpectedVersion);
    public sealed record MemoryRequest(long ExpectedVersion, [MaxLength(2000)] string? ReviewedSummary, AiCustomerMemoryDetails? Details = null);
    public sealed record ReviewRequest(bool Accept);

    [HttpGet("improvements"), Authorize(Roles = "1")]
    public Task<IActionResult> Improvements([FromQuery] Guid accountId, CancellationToken ct) => Run(async () =>
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await improvements.ListAsync(accountId, ct));
    });

    [HttpPost("improvements"), Authorize(Roles = "1")]
    public Task<IActionResult> ProposeImprovement(AiImprovementRequest request, CancellationToken ct) => Run(async () =>
        Ok(await improvements.ProposeAsync(request, Actor, Request.Headers["Idempotency-Key"].ToString(), ct)));

    [HttpGet("status")]
    public IActionResult Status() => Ok(new { options.Value.DraftsEnabled, options.Value.ExternalProcessingApproved,
        options.Value.MaxDraftsPerDay, options.Value.Model, sendsAvailable = false });

    [HttpGet("knowledge"), Authorize(Roles = "1")]
    public async Task<IActionResult> Knowledge([FromQuery] Guid accountId, CancellationToken ct) => Ok(await db.AiSalesKnowledge.AsNoTracking()
        .Where(k => k.TenantId == Tenant && k.WhatsAppAccountId == accountId).OrderByDescending(k => k.CreatedAt).Take(100).ToListAsync(ct));

    [HttpPost("knowledge"), Authorize(Roles = "1")]
    public Task<IActionResult> CreateKnowledge(KnowledgeRequest request, CancellationToken ct) => Run(async () =>
    {
        controls.RequireEnabled();
        if (request.Kind is not (AiKnowledgeKind.Fact or AiKnowledgeKind.Playbook) || request.ValidUntil <= request.ValidFrom ||
            request.ValidUntil <= DateTime.UtcNow || request.ValidUntil - request.ValidFrom > TimeSpan.FromDays(90))
            throw new AiSalesValidation("Select Fact or Playbook and a future expiry, no more than 90 days after validity starts.");
        if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Content) || string.IsNullOrWhiteSpace(request.SourceReference))
            throw new AiSalesValidation("Title, content and source reference are required.");
        if (!await db.WhatsAppAccounts.AnyAsync(a => a.Id == request.WhatsAppAccountId && a.IsActive, ct))
            throw new AiSalesValidation("Account unavailable.");
        await using var accountGate = await ConversationLock.AcquireAsync(db, "knowledge-account:" + Tenant + ":" + request.WhatsAppAccountId, ct);
        var item = new AiSalesKnowledge { TenantId = Tenant, WhatsAppAccountId = request.WhatsAppAccountId,
            Kind = request.Kind, Title = request.Title.Trim(), Content = request.Content.Trim(), SourceReference = request.SourceReference.Trim(),
            ValidFrom = request.ValidFrom.ToUniversalTime(), ValidUntil = request.ValidUntil.ToUniversalTime(), CreatedById = Actor };
        db.AiSalesKnowledge.Add(item); Audit(item.Id, "AiKnowledgeCreated"); await db.SaveChangesAsync(ct); return Ok(item);
    });

    [HttpPost("knowledge/{id:guid}/{operation:regex(^(approve|retire)$)}"), Authorize(Roles = "1")]
    public Task<IActionResult> ChangeKnowledge(Guid id, string operation, VersionRequest request, CancellationToken ct) => Run(async () =>
    {
        var account = await db.AiSalesKnowledge.AsNoTracking().Where(k => k.Id == id && k.TenantId == Tenant).Select(k => (Guid?)k.WhatsAppAccountId).SingleOrDefaultAsync(ct);
        if (account == null) return NotFound();
        await using var accountGate = await ConversationLock.AcquireAsync(db, "knowledge-account:" + Tenant + ":" + account.Value, ct);
        await using var gate = await ConversationLock.AcquireAsync(db, "knowledge:" + id, ct);
        var item = await db.AiSalesKnowledge.SingleOrDefaultAsync(k => k.Id == id && k.TenantId == Tenant, ct);
        if (item == null) return NotFound();
        if (item.Version != request.ExpectedVersion) throw new AiSalesConflict("Knowledge changed; refresh first.");
        if (operation == "approve")
        {
            controls.RequireEnabled();
            if (item.Status != AiKnowledgeStatus.Draft || item.ValidUntil <= DateTime.UtcNow)
                throw new AiSalesValidation("Only unexpired draft knowledge can be approved.");
            item.Status = AiKnowledgeStatus.Approved; item.ApprovedById = Actor; item.ApprovedAt = DateTime.UtcNow;
        }
        else item.Status = AiKnowledgeStatus.Retired;
        item.Version++; Audit(item.Id, operation == "approve" ? "AiKnowledgeApproved" : "AiKnowledgeRetired");
        await db.SaveChangesAsync(ct); return Ok(item);
    });

    [HttpGet("conversations/{id:guid}/memory")]
    public async Task<IActionResult> Memory(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (!await CanSee(id, ct)) return NotFound();
        return Ok(await db.AiCustomerMemories.AsNoTracking().SingleOrDefaultAsync(m => m.ConversationId == id && m.TenantId == Tenant, ct));
    }

    [HttpPut("conversations/{id:guid}/memory")]
    public Task<IActionResult> UpdateMemory(Guid id, MemoryRequest request, CancellationToken ct) => Run(async () =>
    {
        if (!await CanSee(id, ct)) return NotFound();
        await using var gate = await ConversationLock.AcquireAsync(db, "conversation:" + id, ct);
        if (!await db.ConversationControls.AnyAsync(c => c.ConversationId == id && c.TenantId == Tenant, ct))
            throw new AiSalesValidation("No scoped AI control record.");
        var memory = await db.AiCustomerMemories.SingleOrDefaultAsync(m => m.ConversationId == id, ct);
        if (memory != null && memory.TenantId != Tenant) return NotFound();
        if ((memory?.Version ?? 0) != request.ExpectedVersion) throw new AiSalesConflict("Memory changed; refresh first.");
        var summary = request.ReviewedSummary?.Trim() ?? memory?.ReviewedSummary ?? "";
        if (summary.Length > 2000) throw new AiSalesValidation("Reviewed summary exceeds its limit.");
        var details = request.Details?.Normalize() ?? AiCustomerMemoryDetails.Read(memory?.ReviewedDetailsJson ?? "{}");
        if (summary.Length == 0 && !details.HasContent && memory == null) throw new AiSalesValidation("Enter reviewed customer context before saving.");
        if (memory == null) { memory = new() { ConversationId = id, TenantId = Tenant, Version = 0 }; db.AiCustomerMemories.Add(memory); }
        memory.ReviewedSummary = summary; memory.ReviewedDetailsJson = details.ToJson(); memory.ReviewedById = Actor; memory.Version++; memory.UpdatedAt = DateTime.UtcNow;
        Audit(id, "AiMemoryReviewed"); await db.SaveChangesAsync(ct); return Ok(memory);
    });

    [HttpGet("conversations/{id:guid}/drafts")]
    public async Task<IActionResult> Drafts(Guid id, CancellationToken ct)
    {
        if (!await CanSee(id, ct)) return NotFound();
        var items = await db.AiSalesDrafts.AsNoTracking().Where(d => d.TenantId == Tenant && d.ConversationId == id)
            .OrderByDescending(d => d.CreatedAt).Take(10).ToListAsync(ct);
        var result = new List<object>();
        foreach (var draft in items)
        {
            var ids = JsonSerializer.Deserialize<Guid[]>(draft.SourceIdsJson) ?? [];
            var sources = await db.AiSalesKnowledge.AsNoTracking().Where(k => k.TenantId == Tenant && ids.Contains(k.Id))
                .Select(k => new { k.Id, k.Title, k.SourceReference, k.ValidUntil, k.Status, k.Version }).ToListAsync(ct);
            result.Add(new { draft, sources, current = (draft.Status is AiDraftStatus.ReviewRequired or AiDraftStatus.Reviewed) && await drafts.IsCurrentAsync(draft, ct) });
        }
        return Ok(result);
    }

    [HttpPost("conversations/{id:guid}/drafts")]
    public Task<IActionResult> Generate(Guid id, VersionRequest request, CancellationToken ct) => Run(async () =>
    {
        if (!await CanSee(id, ct)) return NotFound();
        return Ok(await drafts.GenerateAsync(id, Actor, request.ExpectedVersion, Request.Headers["Idempotency-Key"].ToString(), ct));
    });

    [HttpPost("drafts/{id:guid}/review")]
    public Task<IActionResult> Review(Guid id, ReviewRequest request, CancellationToken ct) => Run(async () =>
    {
        var chatId = await db.AiSalesDrafts.Where(d => d.Id == id && d.TenantId == Tenant).Select(d => (Guid?)d.ConversationId).SingleOrDefaultAsync(ct);
        if (chatId == null || !await CanSee(chatId.Value, ct)) return NotFound();
        return Ok(await drafts.ReviewAsync(id, Actor, request.Accept, ct));
    });

    private async Task<bool> CanSee(Guid id, CancellationToken ct) => await db.WhatsAppConversations
        .ApplyVisibility(await WhatsAppScope.ForUserAsync(db, User.FindFirstValue(ClaimTypes.Role), Actor, ct)).AnyAsync(c => c.Id == id, ct);
    private void Audit(Guid id, string action) => db.AuditLogs.Add(new() { Id = Guid.NewGuid(), ActorId = Actor, ActorType = 1,
        EventType = action, EntityType = "AiSalesEngine", EntityId = id, CreatedAt = DateTime.UtcNow });
    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (AiSalesValidation ex) { return UnprocessableEntity(new { error = ex.Message }); }
        catch (AiSalesConflict ex) { return Conflict(new { error = ex.Message }); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { error = "State changed; refresh before retrying." }); }
    }
}
