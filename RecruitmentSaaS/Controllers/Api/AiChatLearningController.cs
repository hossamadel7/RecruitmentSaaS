using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Services;
using System.Security.Claims;

namespace RecruitmentSaaS.Controllers.Api;

[ApiController, Route("api/ai-chat-learning"), Authorize(Roles = "1"), AutoValidateAntiforgeryToken]
public sealed class AiChatLearningController(AiChatLearningService preparation, AiChatFactService facts, RecruitmentCrmContext db, IOptions<AiChatLearningOptions> options) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public sealed record PrepareRequest(Guid AccountId, Guid? AfterMessageId = null, DateTime? SnapshotBefore = null, int PageSize = 100);
    [HttpGet("status")]
    public IActionResult Status() => Ok(new { options.Value.Enabled, options.Value.ApprovedAccountIds, externalTrainingAvailable = false, automaticLearning = false });
    [HttpGet("accounts")]
    public async Task<IActionResult> Accounts(CancellationToken ct) => Ok(await db.WhatsAppAccounts.AsNoTracking()
        .Where(a => options.Value.Enabled && options.Value.ApprovedAccountIds.Contains(a.Id) && a.IsActive).Select(a => new { a.Id, a.Name }).ToListAsync(ct));
    [HttpPost("extract")]
    public Task<IActionResult> Extract(PrepareRequest request, CancellationToken ct) => Run(async () => Ok(await facts.ExtractAsync(request.AccountId,
        request.AfterMessageId, request.SnapshotBefore, request.PageSize, Actor, ct)));
    [HttpGet("facts/next")]
    public Task<IActionResult> Next([FromQuery] Guid accountId, CancellationToken ct) => Run(async () => Ok(await facts.NextAsync(accountId, ct)));
    [HttpPost("facts/{id:guid}/review")]
    public Task<IActionResult> Review(Guid id, AiChatFactDecision decision, CancellationToken ct) => Run(async () => Ok(await facts.DecideAsync(id, Actor, decision, ct)));
    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        Response.Headers.CacheControl = "no-store";
        try { return await action(); }
        catch (AiSalesValidation ex) { return UnprocessableEntity(new { error = ex.Message }); }
        catch (AiSalesConflict ex) { return Conflict(new { error = ex.Message }); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { error = "State changed; refresh before reviewing." }); }
    }
    [HttpPost("prepare")]
    public async Task<IActionResult> Prepare(PrepareRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try { return Ok(await preparation.PrepareAsync(request.AccountId, request.AfterMessageId, request.SnapshotBefore, request.PageSize,
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), ct)); }
        catch (AiSalesValidation ex) { return UnprocessableEntity(new { error = ex.Message }); }
    }
}
