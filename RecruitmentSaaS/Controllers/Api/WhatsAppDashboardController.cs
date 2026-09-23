using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.Security.Claims;

namespace RecruitmentSaaS.Controllers.Api
{
    [Route("api/whatsapp/dashboard")]
    [ApiController]
    [Authorize(Roles = WhatsAppAuthorization.AllowedRoles)]
    public class WhatsAppDashboardController : ControllerBase
    {
        private readonly RecruitmentCrmContext _context;

        public WhatsAppDashboardController(RecruitmentCrmContext context)
        {
            _context = context;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        private string? CurrentRole => User.FindFirstValue(ClaimTypes.Role);

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var todayStart = DateTime.UtcNow.Date;
            var isOrgWide = WhatsAppAuthorization.IsOrgWide(CurrentRole);
            var userId = CurrentUserId;

            var conversations = _context.WhatsAppConversations.AsNoTracking().AsQueryable();
            if (!isOrgWide) conversations = conversations.Where(c => c.AssignedSalesAgentId == userId);

            var messagesToday = _context.WhatsAppMessages.AsNoTracking().Where(m => m.CreatedAt >= todayStart);
            if (!isOrgWide) messagesToday = messagesToday.Where(m => m.Conversation.AssignedSalesAgentId == userId);

            var today = new
            {
                IncomingMessages = await messagesToday.CountAsync(m => m.Direction == (byte)MessageDirection.Incoming),
                OutgoingMessages = await messagesToday.CountAsync(m => m.Direction == (byte)MessageDirection.Outgoing),
                NewConversations = await conversations.CountAsync(c => c.CreatedAt >= todayStart),
                UnreadConversations = await conversations.CountAsync(c => c.UnreadCount > 0),
                OpenConversations = await conversations.CountAsync(c =>
                    c.Status == (byte)ConversationStatus.Open || c.Status == (byte)ConversationStatus.New || c.Status == (byte)ConversationStatus.WaitingForCustomer),
                QualifiedCount = await conversations.CountAsync(c => c.LeadStage == (byte)LeadStage.Qualified),
                WonCount = await conversations.CountAsync(c => c.LeadStage == (byte)LeadStage.Won),
                LostCount = await conversations.CountAsync(c => c.LeadStage == (byte)LeadStage.Lost),
                FollowUpsDueToday = await _context.ConversationFollowUps.AsNoTracking()
                    .Where(f => f.Status == (byte)FollowUpStatus.Pending && f.DueAt < todayStart.AddDays(1) &&
                        (isOrgWide || f.AssignedToId == userId))
                    .CountAsync(),
                HandoffsGenerated = await _context.WhatsAppHandoffs.AsNoTracking().CountAsync(h => h.CreatedAt >= todayStart),
                HandoffsConnected = await _context.WhatsAppHandoffs.AsNoTracking()
                    .CountAsync(h => h.ConnectedAt != null && h.ConnectedAt >= todayStart)
            };

            var perAccount = await _context.WhatsAppAccounts.AsNoTracking()
                .Where(a => a.IsActive)
                .Select(a => new
                {
                    a.Id,
                    a.Name,
                    AssignedAgentName = a.AssignedSalesAgent != null ? a.AssignedSalesAgent.FullName : null,
                    IncomingMessagesToday = _context.WhatsAppMessages.Count(m =>
                        m.WhatsAppAccountId == a.Id && m.CreatedAt >= todayStart && m.Direction == (byte)MessageDirection.Incoming),
                    OpenConversations = _context.WhatsAppConversations.Count(c =>
                        c.WhatsAppAccountId == a.Id &&
                        (c.Status == (byte)ConversationStatus.Open || c.Status == (byte)ConversationStatus.New || c.Status == (byte)ConversationStatus.WaitingForCustomer)),
                    UnreadConversations = _context.WhatsAppConversations.Count(c => c.WhatsAppAccountId == a.Id && c.UnreadCount > 0),
                    WonCount = _context.WhatsAppConversations.Count(c => c.WhatsAppAccountId == a.Id && c.LeadStage == (byte)LeadStage.Won)
                })
                .ToListAsync();

            object? perAgent = null;
            if (isOrgWide)
            {
                perAgent = await _context.Users.AsNoTracking()
                    .Where(u => u.IsActive && (u.Role == 6 || u.Role == 3))
                    .Select(u => new
                    {
                        u.Id,
                        u.FullName,
                        AssignedConversations = _context.WhatsAppConversations.Count(c => c.AssignedSalesAgentId == u.Id),
                        OpenConversations = _context.WhatsAppConversations.Count(c => c.AssignedSalesAgentId == u.Id &&
                            (c.Status == (byte)ConversationStatus.Open || c.Status == (byte)ConversationStatus.New || c.Status == (byte)ConversationStatus.WaitingForCustomer)),
                        QualifiedCount = _context.WhatsAppConversations.Count(c => c.AssignedSalesAgentId == u.Id && c.LeadStage == (byte)LeadStage.Qualified),
                        WonCount = _context.WhatsAppConversations.Count(c => c.AssignedSalesAgentId == u.Id && c.LeadStage == (byte)LeadStage.Won),
                        LostCount = _context.WhatsAppConversations.Count(c => c.AssignedSalesAgentId == u.Id && c.LeadStage == (byte)LeadStage.Lost),
                        OverdueFollowUps = _context.ConversationFollowUps.Count(f =>
                            f.AssignedToId == u.Id && f.Status == (byte)FollowUpStatus.Pending && f.DueAt < DateTime.UtcNow)
                    })
                    .ToListAsync();
            }

            var avgFirstResponseMinutes = await ComputeAverageFirstResponseMinutesAsync(isOrgWide, userId);

            return Ok(new
            {
                today,
                avgFirstResponseMinutes,
                perAccount,
                perAgent
            });
        }

        /// <summary>
        /// Average minutes between a conversation's first incoming message and the first
        /// outgoing message from an agent (never counting automated/system events, since
        /// there are none in this module — only Direction=Outgoing rows written by a real user).
        /// </summary>
        private async Task<double?> ComputeAverageFirstResponseMinutesAsync(bool isOrgWide, Guid userId)
        {
            var conversationsQuery = _context.WhatsAppConversations.AsNoTracking().AsQueryable();
            if (!isOrgWide) conversationsQuery = conversationsQuery.Where(c => c.AssignedSalesAgentId == userId);

            var sample = await conversationsQuery
                .OrderByDescending(c => c.CreatedAt)
                .Take(200)
                .Select(c => new
                {
                    FirstInbound = c.WhatsAppMessages
                        .Where(m => m.Direction == (byte)MessageDirection.Incoming)
                        .OrderBy(m => m.WhatsAppTimestamp)
                        .Select(m => (DateTime?)m.WhatsAppTimestamp)
                        .FirstOrDefault(),
                    FirstOutbound = c.WhatsAppMessages
                        .Where(m => m.Direction == (byte)MessageDirection.Outgoing && m.SenderUserId != null)
                        .OrderBy(m => m.WhatsAppTimestamp)
                        .Select(m => (DateTime?)m.WhatsAppTimestamp)
                        .FirstOrDefault()
                })
                .Where(x => x.FirstInbound != null && x.FirstOutbound != null && x.FirstOutbound > x.FirstInbound)
                .ToListAsync();

            if (sample.Count == 0) return null;

            return sample.Average(x => (x.FirstOutbound!.Value - x.FirstInbound!.Value).TotalMinutes);
        }
    }
}
