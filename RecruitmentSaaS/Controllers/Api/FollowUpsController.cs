using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.DTOs;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.Security.Claims;

namespace RecruitmentSaaS.Controllers.Api
{
    [Route("api/followups")]
    [ApiController]
    [Authorize(Roles = WhatsAppAuthorization.AllowedRoles)]
    public class FollowUpsController : ControllerBase
    {
        private readonly RecruitmentCrmContext _context;

        public FollowUpsController(RecruitmentCrmContext context)
        {
            _context = context;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        private string? CurrentRole => User.FindFirstValue(ClaimTypes.Role);

        // ── GET /api/followups?filter=today|upcoming|overdue|completed ───────
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string filter = "today")
        {
            var userId = CurrentUserId;
            var now = DateTime.UtcNow;
            var todayEnd = DateTime.UtcNow.Date.AddDays(1);

            var query = _context.ConversationFollowUps.AsNoTracking().AsQueryable();

            if (!WhatsAppAuthorization.IsOrgWide(CurrentRole))
                query = query.Where(f => f.AssignedToId == userId);

            query = filter switch
            {
                "overdue" => query.Where(f => f.Status == (byte)FollowUpStatus.Pending && f.DueAt < now),
                "upcoming" => query.Where(f => f.Status == (byte)FollowUpStatus.Pending && f.DueAt >= todayEnd),
                "completed" => query.Where(f => f.Status == (byte)FollowUpStatus.Completed),
                _ => query.Where(f => f.Status == (byte)FollowUpStatus.Pending && f.DueAt < todayEnd) // "today" = due today or already overdue
            };

            var items = await query
                .OrderBy(f => f.DueAt)
                .Select(f => new ConversationFollowUpDto
                {
                    Id = f.Id,
                    ConversationId = f.ConversationId,
                    ContactName = f.Conversation.Contact.Name ?? f.Conversation.Contact.WhatsAppPhoneNumber,
                    WhatsAppAccountName = f.Conversation.WhatsAppAccount.Name,
                    AssignedToId = f.AssignedToId,
                    AssignedToName = f.AssignedTo.FullName,
                    DueAt = f.DueAt,
                    Status = f.Status,
                    Notes = f.Notes,
                    IsOverdue = f.Status == (byte)FollowUpStatus.Pending && f.DueAt < now
                })
                .ToListAsync();

            return Ok(items);
        }

        // ── PATCH /api/followups/{id}/complete ────────────────────────────────
        [HttpPatch("{id:guid}/complete")]
        public async Task<IActionResult> Complete(Guid id)
        {
            var followUp = await _context.ConversationFollowUps.FirstOrDefaultAsync(f => f.Id == id);
            if (followUp == null) return NotFound();

            if (!WhatsAppAuthorization.IsOrgWide(CurrentRole) && followUp.AssignedToId != CurrentUserId)
                return Forbid();

            followUp.Status = (byte)FollowUpStatus.Completed;
            followUp.CompletedAt = DateTime.UtcNow;
            followUp.CompletedById = CurrentUserId;
            await _context.SaveChangesAsync();

            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorId = CurrentUserId,
                ActorType = 1,
                EventType = "FollowUpCompleted",
                EntityType = "ConversationFollowUp",
                EntityId = followUp.Id,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            return Ok(new { success = true });
        }
    }
}
