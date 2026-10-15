using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace RecruitmentSaaS.Controllers.Api;
[ApiController, Route("api/ai-office-bookings"), Authorize(Roles = "1,7,8"), AutoValidateAntiforgeryToken]
public sealed class AiOfficeBookingController(RecruitmentCrmContext db, AiSalesControlService controls, AiOfficeBookingService bookings) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private Guid Tenant => controls.Options.TenantId;
    public sealed record PolicyRequest(long ExpectedVersion, Guid WhatsAppAccountId, Guid BranchId, int OpenDaysMask, int OpensAtMinute,
        int ClosesAtMinute, DateTime ValidUntil, [Required, MaxLength(500)] string SourceReference, bool Enabled);
    public sealed record CancelRequest(long ExpectedOwnershipVersion, long ExpectedBookingVersion, [Required, MaxLength(500)] string Reason);
    [HttpGet("status")]
    public IActionResult Status() => Ok(new { bookings.Options.Enabled, bookings.Options.MaxDaysAhead, automaticCustomerMessages = false });
    [HttpGet("branches"), Authorize(Roles = "1")]
    public async Task<IActionResult> Branches(CancellationToken ct) => Ok(await db.Branches.AsNoTracking().Where(b => b.IsActive).Select(b => new { b.Id, b.Name }).ToListAsync(ct));
    [HttpGet("policies"), Authorize(Roles = "1")]
    public async Task<IActionResult> Policies(Guid accountId, CancellationToken ct) => Ok(await db.AiOfficePolicies.AsNoTracking().Where(p => p.TenantId == Tenant && p.WhatsAppAccountId == accountId).ToListAsync(ct));
    [HttpPost("policies"), Authorize(Roles = "1")]
    public Task<IActionResult> Policy(PolicyRequest request, CancellationToken ct) => Run(async () =>
    {
        controls.RequireEnabled();
        if (request.OpenDaysMask is < 1 or > 127 || request.OpensAtMinute is < 0 or >= 1440 || request.ClosesAtMinute is <= 0 or > 1440 ||
            request.OpensAtMinute >= request.ClosesAtMinute || request.ValidUntil <= DateTime.UtcNow || request.ValidUntil > DateTime.UtcNow.AddDays(90) ||
            string.IsNullOrWhiteSpace(request.SourceReference)) throw new AiSalesValidation("Verify office days, hours, reference and a future expiry within 90 days.");
        if (!await db.Branches.AnyAsync(b => b.Id == request.BranchId && b.IsActive, ct) || !await db.WhatsAppAccounts.AnyAsync(a => a.Id == request.WhatsAppAccountId && a.IsActive, ct))
            throw new AiSalesValidation("Select an active branch and WhatsApp account.");
        await using var gate = await ConversationLock.AcquireAsync(db, "office-policy:" + Tenant + ":" + request.BranchId, ct);
        var policy = await db.AiOfficePolicies.SingleOrDefaultAsync(p => p.TenantId == Tenant && p.WhatsAppAccountId == request.WhatsAppAccountId && p.BranchId == request.BranchId, ct);
        if ((policy?.Version ?? 0) != request.ExpectedVersion) throw new AiSalesConflict("Office hours changed; refresh first.");
        if (policy == null) { policy = new() { TenantId = Tenant, WhatsAppAccountId = request.WhatsAppAccountId, BranchId = request.BranchId, Version = 0 }; db.AiOfficePolicies.Add(policy); }
        policy.Version++; policy.OpenDaysMask = request.OpenDaysMask; policy.OpensAtMinute = request.OpensAtMinute; policy.ClosesAtMinute = request.ClosesAtMinute;
        policy.ValidUntil = request.ValidUntil.ToUniversalTime(); policy.Enabled = request.Enabled; policy.SourceReference = request.SourceReference.Trim(); policy.ApprovedById = Actor; policy.ApprovedAt = DateTime.UtcNow;
        db.AuditLogs.Add(new() { Id = Guid.NewGuid(), ActorId = Actor, ActorType = 1, EventType = "AiOfficePolicyApproved", EntityType = "AiOfficePolicy", EntityId = policy.Id,
            NewValueJson = System.Text.Json.JsonSerializer.Serialize(policy), CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(ct); return Ok(policy);
    });
    [HttpGet("conversations/{id:guid}")]
    public async Task<IActionResult> List(Guid id, CancellationToken ct)
    {
        if (!await CanSee(id, ct)) return NotFound();
        var chat = await db.WhatsAppConversations.AsNoTracking().SingleAsync(c => c.Id == id, ct);
        var items = await db.AiOfficeBookings.AsNoTracking().Where(b => b.TenantId == Tenant && b.ConversationId == id).OrderByDescending(b => b.CreatedAt).Take(20).ToListAsync(ct);
        var ids = items.Select(b => b.Id).ToArray();
        var history = await db.AiOfficeBookingEvents.AsNoTracking().Where(e => e.TenantId == Tenant && ids.Contains(e.BookingId)).OrderByDescending(e => e.At).Take(100).ToListAsync(ct);
        var policies = await db.AiOfficePolicies.AsNoTracking().Where(p => p.TenantId == Tenant && p.WhatsAppAccountId == chat.WhatsAppAccountId && p.Enabled && p.ValidUntil > DateTime.UtcNow)
            .Join(db.Branches.Where(b => b.IsActive), p => p.BranchId, b => b.Id, (p, b) => new { policy = p, branchName = b.Name, b.Address }).ToListAsync(ct);
        var since = DateTime.UtcNow.AddDays(-1);
        var evidence = await db.WhatsAppMessages.AsNoTracking().Where(m => m.ConversationId == id && m.Direction == (byte)MessageDirection.Incoming && m.WhatsAppTimestamp >= since &&
            (m.MessageType == (byte)WhatsAppMessageType.Text || m.MessageType == (byte)WhatsAppMessageType.Audio &&
                db.AiVoiceParts.Any(p => p.MessageId == m.Id && db.AiVoiceTurns.Any(t => t.Id == p.TurnId && t.TenantId == Tenant && t.Status == AiVoiceStatus.Reviewed))))
            .OrderByDescending(m => m.CreatedAt).Take(25).Select(m => new { m.Id, m.CreatedAt, m.MessageType, m.TextBody }).ToListAsync(ct);
        return Ok(new { items, history, policies, evidence });
    }
    [HttpPost("conversations/{id:guid}")]
    public Task<IActionResult> Create(Guid id, OfficeBookingRequest request, CancellationToken ct) => Run(async () =>
    {
        if (!await CanSee(id, ct)) return NotFound();
        return Ok(await bookings.SaveAsync(id, null, Actor, request, Request.Headers["Idempotency-Key"].ToString(), ct));
    });
    [HttpPost("{id:guid}/reschedule")]
    public Task<IActionResult> Reschedule(Guid id, OfficeBookingRequest request, CancellationToken ct) => Run(async () =>
    {
        var chatId = await ChatOf(id, ct); if (chatId == null || !await CanSee(chatId.Value, ct)) return NotFound();
        return Ok(await bookings.SaveAsync(chatId.Value, id, Actor, request, Request.Headers["Idempotency-Key"].ToString(), ct));
    });
    [HttpPost("{id:guid}/cancel")]
    public Task<IActionResult> Cancel(Guid id, CancelRequest request, CancellationToken ct) => Run(async () =>
    {
        var chatId = await ChatOf(id, ct); if (chatId == null || !await CanSee(chatId.Value, ct)) return NotFound();
        return Ok(await bookings.CancelAsync(id, Actor, request.ExpectedOwnershipVersion, request.ExpectedBookingVersion, request.Reason, Request.Headers["Idempotency-Key"].ToString(), ct));
    });
    private Task<Guid?> ChatOf(Guid id, CancellationToken ct) => db.AiOfficeBookings.Where(b => b.Id == id && b.TenantId == Tenant).Select(b => (Guid?)b.ConversationId).SingleOrDefaultAsync(ct);
    private async Task<bool> CanSee(Guid id, CancellationToken ct) => await db.WhatsAppConversations.ApplyVisibility(await WhatsAppScope.ForUserAsync(db, User.FindFirstValue(ClaimTypes.Role), Actor, ct)).AnyAsync(c => c.Id == id, ct);
    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (AiSalesValidation ex) { return UnprocessableEntity(new { error = ex.Message }); }
        catch (AiSalesConflict ex) { return Conflict(new { error = ex.Message }); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { error = "State changed; refresh first." }); }
    }
}
