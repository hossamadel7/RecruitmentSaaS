using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Services;
using System.Security.Claims;

namespace RecruitmentSaaS.Controllers.Api;

[ApiController, Route("api/ai-sales-dispatch"), Authorize(Roles = "1"), AutoValidateAntiforgeryToken]
public sealed class AiSalesDispatchController(RecruitmentCrmContext db, AiSalesControlService controls,
    AiSalesDispatchService dispatch, IOptions<AiSalesDispatchOptions> options) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public sealed record QueueRequest(Guid DraftId, long ExpectedOwnershipVersion);
    [HttpGet("status")]
    public async Task<IActionResult> Status([FromQuery] Guid? conversationId, CancellationToken ct)
    {
        var o = options.Value;
        var account = conversationId == null ? null : await db.WhatsAppConversations.AsNoTracking()
            .Where(c => c.Id == conversationId).Select(c => (Guid?)c.WhatsAppAccountId).SingleOrDefaultAsync(ct);
        return Ok(new { o.Enabled, o.ReleaseApproved, o.MaxAttemptsPerDay,
            sendingAvailable = controls.Options.Enabled && account != null && o.Enabled && o.ReleaseApproved &&
                o.ApprovedAccountIds.Contains(account.Value) && o.ApprovedConversationIds.Contains(conversationId!.Value),
            automaticDispatch = false, templatesAvailable = false });
    }
    [HttpGet("conversations/{id:guid}")]
    public async Task<IActionResult> List(Guid id, CancellationToken ct) => Ok(await db.AiOutboundIntents.AsNoTracking()
        .Where(i => i.TenantId == controls.Options.TenantId && i.ConversationId == id).OrderByDescending(i => i.CreatedAt).Take(20).ToListAsync(ct));
    [HttpPost]
    public Task<IActionResult> Queue(QueueRequest request, CancellationToken ct) => Run(async () => Ok(await dispatch.QueueAsync(
        request.DraftId, Actor, request.ExpectedOwnershipVersion, Request.Headers["Idempotency-Key"].ToString(), ct)));
    [HttpPost("{id:guid}/send")]
    public Task<IActionResult> Send(Guid id, CancellationToken ct) => Run(async () => Ok(await dispatch.DispatchAsync(id, Actor, ct)));
    [HttpPost("{id:guid}/cancel")]
    public Task<IActionResult> Cancel(Guid id, CancellationToken ct) => Run(async () => Ok(await dispatch.CancelAsync(id, Actor, ct)));
    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (AiSalesValidation ex) { return UnprocessableEntity(new { error = ex.Message }); }
        catch (AiSalesConflict ex) { return Conflict(new { error = ex.Message }); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { error = "State changed; refresh before retrying." }); }
    }
}
