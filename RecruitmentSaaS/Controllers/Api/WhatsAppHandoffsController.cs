using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.DTOs;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.Security.Claims;
using System.Security.Cryptography;

namespace RecruitmentSaaS.Controllers.Api
{
    /// <summary>
    /// Tracks the manual Messenger → WhatsApp handoff flow. A handoff only becomes "Connected"
    /// once the customer's WhatsApp message referencing it actually reaches our webhook —
    /// clicking the wa.me link alone is not enough (see WhatsAppWebhookProcessor.TryConnectHandoffAsync).
    /// </summary>
    [Route("api/leads/{leadId:guid}/handoff")]
    [ApiController]
    [Authorize(Roles = WhatsAppAuthorization.AllowedRoles)]
    public class WhatsAppHandoffsController : ControllerBase
    {
        private readonly RecruitmentCrmContext _context;

        public WhatsAppHandoffsController(RecruitmentCrmContext context)
        {
            _context = context;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        private string? CurrentRole => User.FindFirstValue(ClaimTypes.Role);

        // ── POST /api/leads/{leadId}/handoff ──────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Generate(Guid leadId, [FromBody] GenerateHandoffDto dto)
        {
            if (!WhatsAppAuthorization.CanCreateHandoff(CurrentRole))
                return Forbid();

            var lead = await _context.Leads.FirstOrDefaultAsync(l => l.Id == leadId);
            if (lead == null) return NotFound(new { error = "Lead not found." });

            var account = await _context.WhatsAppAccounts.FirstOrDefaultAsync(a => a.Id == dto.WhatsAppAccountId && a.IsActive);
            if (account == null) return BadRequest(new { error = "WhatsApp account not found or inactive." });

            var assignedAgentId = dto.AssignedSalesAgentId ?? account.AssignedSalesAgentId;
            if (assignedAgentId == null)
                return BadRequest(new { error = "This account has no default sales agent — pick one explicitly." });

            var referenceCode = await GenerateUniqueReferenceCodeAsync();
            var waUrl = $"https://wa.me/{account.DisplayPhoneNumber.TrimStart('+')}" +
                        $"?text={Uri.EscapeDataString($"Hello, I'm interested in getting more information. Reference: #{referenceCode}")}";

            var handoff = new WhatsAppHandoff
            {
                Id = Guid.NewGuid(),
                LeadId = leadId,
                WhatsAppAccountId = account.Id,
                AssignedSalesAgentId = assignedAgentId.Value,
                ReferenceCode = referenceCode,
                GeneratedWhatsAppUrl = waUrl,
                Status = (byte)HandoffStatus.Generated,
                CreatedAt = DateTime.UtcNow
            };
            _context.WhatsAppHandoffs.Add(handoff);

            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                ActorId = CurrentUserId,
                ActorType = 1,
                EventType = "HandoffGenerated",
                EntityType = "WhatsAppHandoff",
                EntityId = handoff.Id,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            return Ok(new WhatsAppHandoffDto
            {
                Id = handoff.Id,
                ReferenceCode = handoff.ReferenceCode,
                GeneratedWhatsAppUrl = handoff.GeneratedWhatsAppUrl,
                Status = handoff.Status,
                LeadId = lead.Id,
                LeadFullName = lead.FullName,
                WhatsAppAccountName = account.Name,
                CreatedAt = handoff.CreatedAt
            });
        }

        // ── GET /api/leads/{leadId}/handoff ───────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ListForLead(Guid leadId)
        {
            var handoffs = await _context.WhatsAppHandoffs
                .AsNoTracking()
                .Where(h => h.LeadId == leadId)
                .OrderByDescending(h => h.CreatedAt)
                .Select(h => new WhatsAppHandoffDto
                {
                    Id = h.Id,
                    ReferenceCode = h.ReferenceCode,
                    GeneratedWhatsAppUrl = h.GeneratedWhatsAppUrl,
                    Status = h.Status,
                    LeadId = h.LeadId,
                    LeadFullName = h.Lead.FullName,
                    WhatsAppAccountName = h.WhatsAppAccount.Name,
                    AssignedSalesAgentName = h.AssignedSalesAgent.FullName,
                    CreatedAt = h.CreatedAt,
                    ConnectedAt = h.ConnectedAt
                })
                .ToListAsync();

            return Ok(handoffs);
        }

        private async Task<string> GenerateUniqueReferenceCodeAsync()
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // no O/0/I/1 ambiguity

            for (var attempt = 0; attempt < 10; attempt++)
            {
                var suffix = RandomNumberGenerator.GetString(alphabet, 6);
                var code = $"REF-{suffix}";

                var exists = await _context.WhatsAppHandoffs.AnyAsync(h => h.ReferenceCode == code);
                if (!exists) return code;
            }

            throw new InvalidOperationException("Could not generate a unique handoff reference code.");
        }
    }
}
