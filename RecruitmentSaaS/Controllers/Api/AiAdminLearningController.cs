using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
namespace RecruitmentSaaS.Controllers.Api;

[ApiController, Route("api/ai-admin-learning"), Authorize(Roles = "1"), AutoValidateAntiforgeryToken, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AiAdminLearningController(RecruitmentCrmContext db, AiAdminLearningService admin, AiAdminDraftResumer resumer) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try { await admin.RequireAdminAsync(Actor, HttpContext.RequestAborted); return await action(); }
        catch (AiSalesValidation e) { return UnprocessableEntity(new { error = e.Message }); }
        catch (AiSalesConflict e) { return Conflict(new { error = e.Message }); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { error = "تغيرت البيانات؛ حدّث الصفحة." }); }
    }
    [HttpGet("status")]
    public Task<IActionResult> Status(Guid accountId, CancellationToken ct) => Run(async () => {
        if (!await db.WhatsAppAccounts.AnyAsync(a => a.Id == accountId && a.IsActive, ct)) return NotFound();
        if (!admin.Options.PrivateRoutingEnabled) return Ok(new { enabled = false, routingEnabled = false, channel = (object?)null, requests = Array.Empty<object>(), messages = Array.Empty<object>() });
        var c = await db.AiAdminChannels.AsNoTracking().SingleOrDefaultAsync(c => c.TenantId == admin.Tenant && c.WhatsAppAccountId == accountId, ct);
        return Ok(new { admin.Options.Enabled, routingEnabled = true, admin.Options.OutboundApproved, admin.Options.BackgroundEnabled,
            accountApproved = admin.Options.ApprovedAccountIds.Contains(accountId),
            channel = c == null ? null : new { c.Id, c.AdminUserId, phone = "…" + c.Phone[^4..], c.VerifiedAt, c.Paused, c.LastInboundAt, c.Version },
            requests = await db.AiAdminClarifications.AsNoTracking().Where(r => r.TenantId == admin.Tenant && r.ChannelId == (c == null ? Guid.Empty : c.Id))
                .OrderByDescending(r => r.CreatedAt).Take(100).ToArrayAsync(ct),
            messages = await db.AiAdminMessages.AsNoTracking().Where(m => m.ChannelId == (c == null ? Guid.Empty : c.Id)).OrderByDescending(m => m.CreatedAt)
                .Select(m => new { m.Id, m.RequestId, m.Incoming, m.Text, m.State, m.CreatedAt, m.ErrorCode }).Take(50).ToArrayAsync(ct) });
    });
    public sealed record BindRequest(Guid AccountId, [Required, MaxLength(20)] string Phone);
    [HttpPost("bind")]
    public Task<IActionResult> Bind(BindRequest r, CancellationToken ct) => Run(async () => {
        var result = await admin.BindAsync(r.AccountId, Actor, r.Phone, ct);
        return Ok(new { channelId = result.Channel.Id, verificationCode = result.Code, expiresAt = result.Channel.VerificationExpiresAt });
    });
    public sealed record AccountRequest(Guid AccountId);
    [HttpPost("renew-verification")]
    public Task<IActionResult> Renew(AccountRequest r, CancellationToken ct) => Run(async () => Ok(new { verificationCode = await admin.RenewVerificationAsync(r.AccountId, Actor, ct) }));
    public sealed record PauseRequest(Guid AccountId, long ExpectedVersion, bool Paused);
    [HttpPost("pause")]
    public Task<IActionResult> Pause(PauseRequest r, CancellationToken ct) => Run(async () => {
        if (!admin.Options.PrivateRoutingEnabled) throw new AiSalesValidation("قناة المدير غير معدة.");
        await using var gate = await ConversationLock.AcquireAsync(db, "admin-channel:" + admin.Tenant + ":" + r.AccountId, ct);
        var c = await db.AiAdminChannels.SingleOrDefaultAsync(c => c.TenantId == admin.Tenant && c.WhatsAppAccountId == r.AccountId && c.AdminUserId == Actor, ct)
            ?? throw new AiSalesValidation("قناة المدير غير متاحة.");
        if (c.Version != r.ExpectedVersion) throw new AiSalesConflict("تغيرت القناة؛ حدّث الصفحة.");
        if (!r.Paused && c.VerifiedAt == null) throw new AiSalesValidation("وثق رقم المدير أولًا.");
        c.Paused = r.Paused; c.Version++; admin.Audit(r.Paused ? "AdminCoachingPaused" : "AdminCoachingResumed", c.Id, Actor);
        await db.SaveChangesAsync(ct); return Ok();
    });
    public sealed record AskRequest(Guid ConversationId, [MaxLength(1800)] string? Question);
    [HttpPost("ask")]
    public Task<IActionResult> Ask(AskRequest r, CancellationToken ct) => Run(async () =>
        Ok(await admin.AskAsync(r.ConversationId, r.Question, Actor, ct) ?? throw new AiSalesValidation("يلزم مساعد مُسند ونشط وقناة مدير موثقة وسؤال نصي جديد.")));
    public sealed record AnswerRequest(long ExpectedVersion, [Required, MaxLength(3000)] string Answer, AiAdminAnswerScope Scope,
        DateTime ValidUntil, bool Publish, [MaxLength(150)] string Title = "", Guid? ReplacesKnowledgeId = null);
    [HttpPost("requests/{id:guid}/validate")]
    public Task<IActionResult> Validate(Guid id, AnswerRequest r, CancellationToken ct) => Run(async () => {
        await admin.ValidateAnswerAsync(id, Actor, r.ExpectedVersion, r.Answer, r.Scope, r.ValidUntil, r.Publish, r.Title, r.ReplacesKnowledgeId, ct); return Ok();
    });
    [HttpPost("requests/{id:guid}/prepare")]
    public Task<IActionResult> Prepare(Guid id, CancellationToken ct) => Run(async () => Ok(await resumer.PrepareAsync(id, ct)));
    public sealed record VersionRequest(long ExpectedVersion);
    [HttpPost("requests/{id:guid}/cancel")]
    public Task<IActionResult> Cancel(Guid id, VersionRequest r, CancellationToken ct) => Run(async () => {
        var request = await db.AiAdminClarifications.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == admin.Tenant, ct) ?? throw new AiSalesValidation("السؤال غير متاح.");
        var channel = await db.AiAdminChannels.SingleAsync(c => c.Id == request.ChannelId, ct);
        await using var gate = await ConversationLock.AcquireAsync(db, "admin-channel:" + admin.Tenant + ":" + channel.WhatsAppAccountId, ct);
        await db.Entry(request).ReloadAsync(ct);
        if (request.Version != r.ExpectedVersion) throw new AiSalesConflict("السؤال تغير.");
        admin.Change(request, AiAdminRequestState.Cancelled, Actor); await db.SaveChangesAsync(ct); return Ok();
    });
    [HttpPost("pump")]
    public Task<IActionResult> Pump(AccountRequest r, CancellationToken ct) => Run(async () => {
        admin.Require(r.AccountId);
        var c = await db.AiAdminChannels.SingleOrDefaultAsync(c => c.TenantId == admin.Tenant && c.WhatsAppAccountId == r.AccountId && c.AdminUserId == Actor, ct)
            ?? throw new AiSalesValidation("القناة غير متاحة.");
        await admin.QueueQuestionsAsync(c.Id, ct);
        var ids = await db.AiAdminMessages.Where(m => m.ChannelId == c.Id && !m.Incoming && (m.State == "Queued" || m.State == "Sending")).OrderBy(m => m.CreatedAt).Select(m => m.Id).Take(10).ToArrayAsync(ct);
        foreach (var id in ids) await admin.DeliverAsync(id, ct);
        return Ok(new { pendingTransport = !admin.Options.OutboundApproved });
    });
}
