using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace RecruitmentSaaS.Controllers.Api;

[ApiController, Route("api/ai-sales-voice"), Authorize(Roles = "1,7,8"), AutoValidateAntiforgeryToken]
public sealed class AiVoiceController(RecruitmentCrmContext db, AiSalesControlService controls, AiVoiceService voice) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private Guid Tenant => controls.Options.TenantId;
    public sealed record QueueRequest(long ExpectedVersion, [Required] Guid[] MessageIds);
    public sealed record ReviewRequest(long ExpectedVersion, [Required, MaxLength(4000)] string ReviewedText, bool CriticalDetailsVerified);
    public sealed record RetryRequest(long ExpectedVersion, bool ConfirmProviderRetry);
    [HttpGet("status")]
    public IActionResult Status() => Ok(new { voice.Options.Enabled, voice.Options.ExternalProcessingApproved, voice.Options.AutoQueueIncoming,
        voice.Options.MaxPartsPerTurn, voice.Options.MaxMessageAgeMinutes, voice.Options.MaxProviderAttemptsPerDay });

    [HttpGet("conversations/{id:guid}")]
    public async Task<IActionResult> List(Guid id, CancellationToken ct)
    {
        if (!await CanSee(id, ct)) return NotFound();
        var turns = await db.AiVoiceTurns.AsNoTracking().Where(t => t.TenantId == Tenant && t.ConversationId == id)
            .OrderByDescending(t => t.CreatedAt).Take(20).ToListAsync(ct);
        var turnIds = turns.Select(t => t.Id).ToArray();
        // Media tokens and provider URLs are never exposed to the browser.
        var parts = await db.AiVoiceParts.AsNoTracking().Where(p => turnIds.Contains(p.TurnId)).Select(p => new {
            p.MessageId, p.TurnId, p.Status, p.Transcript, p.LowestTokenProbability, p.Model, p.InputTokens, p.OutputTokens, p.ErrorCode }).ToListAsync(ct);
        var since = DateTime.UtcNow.AddMinutes(-voice.Options.MaxMessageAgeMinutes);
        var candidates = await db.WhatsAppMessages.AsNoTracking().Where(m => m.ConversationId == id && m.Direction == (byte)MessageDirection.Incoming &&
            m.MessageType == (byte)WhatsAppMessageType.Audio && m.MediaId != null && m.WhatsAppTimestamp >= since &&
            !db.AiVoiceParts.Any(p => p.MessageId == m.Id)).OrderByDescending(m => m.WhatsAppTimestamp).Take(20)
            .Select(m => new { m.Id, m.WhatsAppTimestamp }).ToListAsync(ct);
        return Ok(new { turns, parts, candidates });
    }
    [HttpPost("conversations/{id:guid}/queue")]
    public Task<IActionResult> Queue(Guid id, QueueRequest request, CancellationToken ct) => Run(async () =>
    {
        if (!await CanSee(id, ct)) return NotFound();
        return Ok(await voice.QueueAsync(id, Actor, request.ExpectedVersion, request.MessageIds, ct));
    });
    [HttpPost("turns/{id:guid}/review")]
    public Task<IActionResult> Review(Guid id, ReviewRequest request, CancellationToken ct) => Run(async () =>
    {
        if (!await CanSeeTurn(id, ct)) return NotFound();
        return Ok(await voice.ReviewAsync(id, Actor, request.ExpectedVersion, request.ReviewedText, request.CriticalDetailsVerified, ct));
    });
    [HttpPost("turns/{id:guid}/retry")]
    public Task<IActionResult> Retry(Guid id, RetryRequest request, CancellationToken ct) => Run(async () =>
    {
        if (!await CanSeeTurn(id, ct)) return NotFound();
        return Ok(await voice.RetryAsync(id, Actor, request.ExpectedVersion, request.ConfirmProviderRetry, ct));
    });
    private async Task<bool> CanSee(Guid id, CancellationToken ct) => await db.WhatsAppConversations
        .ApplyVisibility(await WhatsAppScope.ForUserAsync(db, User.FindFirstValue(ClaimTypes.Role), Actor, ct)).AnyAsync(c => c.Id == id, ct);
    private async Task<bool> CanSeeTurn(Guid id, CancellationToken ct)
    {
        var chat = await db.AiVoiceTurns.Where(t => t.Id == id && t.TenantId == Tenant).Select(t => (Guid?)t.ConversationId).SingleOrDefaultAsync(ct);
        return chat != null && await CanSee(chat.Value, ct);
    }
    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (AiSalesValidation ex) { return UnprocessableEntity(new { error = ex.Message }); }
        catch (AiSalesConflict ex) { return Conflict(new { error = ex.Message }); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { error = "State changed; refresh before retrying." }); }
    }
}
