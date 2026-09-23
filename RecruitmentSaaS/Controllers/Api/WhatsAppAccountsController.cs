using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.DTOs;
using RecruitmentSaaS.Services;
using System.Security.Claims;

namespace RecruitmentSaaS.Controllers.Api
{
    [Route("api/whatsapp/accounts")]
    [ApiController]
    [Authorize(Roles = WhatsAppAuthorization.AllowedRoles)]
    public class WhatsAppAccountsController : ControllerBase
    {
        private readonly RecruitmentCrmContext _context;

        public WhatsAppAccountsController(RecruitmentCrmContext context)
        {
            _context = context;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        private string? CurrentRole => User.FindFirstValue(ClaimTypes.Role);

        // ── GET /api/whatsapp/accounts ───────────────────────────────────────
        // Every accessible number, with an unread count scoped to what this user can see,
        // so the inbox's account tabs can render "All / Sales #1 (4) / Sales #2 (7) / ..."
        [HttpGet]
        public async Task<IActionResult> List()
        {
            var isOrgWide = WhatsAppAuthorization.IsOrgWide(CurrentRole);
            var isAdmin = WhatsAppAuthorization.IsAdmin(CurrentRole);
            var userId = CurrentUserId;

            var accountsQuery = _context.WhatsAppAccounts.AsNoTracking();
            if (!isAdmin)
                accountsQuery = accountsQuery.Where(a => a.IsActive);

            var accounts = await accountsQuery
                .OrderBy(a => a.Name)
                .Select(a => new WhatsAppAccountDto
                {
                    Id = a.Id,
                    Name = a.Name,
                    DisplayPhoneNumber = a.DisplayPhoneNumber,
                    PhoneNumberId = isAdmin ? a.PhoneNumberId : null,
                    WabaId = isAdmin ? a.WabaId : null,
                    ConnectionMode = a.ConnectionMode,
                    WebhookSubscriptionStatus = a.WebhookSubscriptionStatus,
                    VerifiedName = a.VerifiedName,
                    IsActive = a.IsActive,
                    AssignedSalesAgentId = a.AssignedSalesAgentId,
                    AssignedSalesAgentName = a.AssignedSalesAgent != null ? a.AssignedSalesAgent.FullName : null,
                    LastMessageAt = _context.WhatsAppConversations
                        .Where(c => c.WhatsAppAccountId == a.Id)
                        .Max(c => (DateTime?)c.LastMessageAt),
                    UnreadCount = _context.WhatsAppConversations.Count(c =>
                        c.WhatsAppAccountId == a.Id &&
                        c.UnreadCount > 0 &&
                        (isOrgWide || c.AssignedSalesAgentId == userId))
                })
                .ToListAsync();

            return Ok(accounts);
        }

        // ── GET /api/whatsapp/accounts/agents ────────────────────────────────
        // The sales-agent roster for the assignment/reassignment dropdown. Only
        // Admin/Supervisor can reassign, so only they need to see the full list.
        [HttpGet("agents")]
        public async Task<IActionResult> ListAgents()
        {
            if (!WhatsAppAuthorization.CanAssignOrTransfer(CurrentRole))
                return Forbid();

            var agents = await _context.Users
                .AsNoTracking()
                .Where(u => u.IsActive && (u.Role == 6 || u.Role == 3))
                .OrderBy(u => u.FullName)
                .Select(u => new { u.Id, u.FullName })
                .ToListAsync();

            return Ok(agents);
        }
    }
}
