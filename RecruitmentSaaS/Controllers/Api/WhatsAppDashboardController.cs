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
                    .Where(u => u.IsActive && (u.Role == 6 || u.Role == 3 || u.Role == 8))
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

        // ── GET /api/whatsapp/dashboard/teams?from=yyyy-MM-dd&to=yyyy-MM-dd ──
        // Conversations and results per team for a range of Egypt days (admin). "Now" columns are a
        // snapshot (waiting to be assigned, unread); everything else counts what happened in the range.
        [HttpGet("teams")]
        public async Task<IActionResult> Teams(DateTime? from, DateTime? to)
        {
            if (!WhatsAppAuthorization.IsOrgWide(CurrentRole)) return Forbid();

            var today = EgyptTime.Today;
            var toDay = (to ?? today).Date;
            var fromDay = (from ?? toDay).Date;
            if (fromDay > toDay) (fromDay, toDay) = (toDay, fromDay);
            if ((toDay - fromDay).TotalDays > 92) fromDay = toDay.AddDays(-92);
            var fromUtc = EgyptTime.ToUtc(fromDay);
            var toUtc = EgyptTime.ToUtc(toDay.AddDays(1));

            // Teams, their TeleSales and their WhatsApp numbers (the number on the team's form)
            var leaders = await _context.Users.AsNoTracking().Where(u => u.Role == 7)
                .OrderBy(u => u.FullName).Select(u => new { u.Id, u.FullName }).ToListAsync();
            var agents = await _context.Users.AsNoTracking().Where(u => u.Role == 3 || u.Role == 8)
                .Select(u => new { u.Id, Name = u.FullNameAr != null && u.FullNameAr != "" ? u.FullNameAr : u.FullName, u.ManagerId, u.IsActive }).ToListAsync();
            var accounts = await _context.WhatsAppAccounts.AsNoTracking().Select(a => new { a.Id, a.Name, a.DisplayPhoneNumber }).ToListAsync();
            var forms = await _context.TeamLeadForms.AsNoTracking().Where(f => f.WhatsAppNumber != null)
                .Select(f => new { f.ManagerId, f.WhatsAppNumber }).ToListAsync();
            static string Digits(string? v) => new string((v ?? "").Where(char.IsDigit).ToArray());
            var teamOfAccount = accounts.ToDictionary(a => a.Id,
                a => forms.FirstOrDefault(f => Digits(f.WhatsAppNumber) == Digits(a.DisplayPhoneNumber))?.ManagerId);
            var teamOfAgent = agents.ToDictionary(a => a.Id, a => a.ManagerId);

            // What happened in the range
            var created = await _context.WhatsAppConversations.AsNoTracking()
                .Where(c => c.CreatedAt >= fromUtc && c.CreatedAt < toUtc)
                .Select(c => new
                {
                    c.WhatsAppAccountId,
                    c.AssignedSalesAgentId,
                    FirstIn = c.WhatsAppMessages.Where(m => m.Direction == (byte)MessageDirection.Incoming).Min(m => (DateTime?)m.WhatsAppTimestamp),
                    FirstHuman = c.WhatsAppMessages.Where(m => m.Direction == (byte)MessageDirection.Outgoing && m.SenderUserId != null).Min(m => (DateTime?)m.WhatsAppTimestamp)
                }).ToListAsync();
            var active = await _context.WhatsAppConversations.AsNoTracking()
                .Where(c => c.WhatsAppMessages.Any(m => m.Direction == (byte)MessageDirection.Incoming && m.WhatsAppTimestamp >= fromUtc && m.WhatsAppTimestamp < toUtc))
                .Select(c => new { c.WhatsAppAccountId, c.AssignedSalesAgentId }).ToListAsync();
            var funnel = await _context.LeadFunnelHistories.AsNoTracking()
                .Where(h => (h.ToStatus == 5 || h.ToStatus == 6 || h.ToStatus == 8) && h.CreatedAt >= fromUtc && h.CreatedAt < toUtc)
                .Select(h => new { h.ToStatus, h.ChangedById, h.LeadId, Owner = h.Lead.AssignedSalesId }).ToListAsync();

            // Right now
            var waiting = await _context.WhatsAppConversations.AsNoTracking().Where(c => c.AssignedSalesAgentId == null && c.AssignedAiSalesAgentId == null)
                .Select(c => new { c.WhatsAppAccountId, c.PendingTeamManagerId }).ToListAsync();
            var unread = await _context.WhatsAppConversations.AsNoTracking().Where(c => c.UnreadCount > 0 && c.AssignedSalesAgentId != null)
                .Select(c => new { Agent = c.AssignedSalesAgentId!.Value }).ToListAsync();

            // A booking / loss counts for who did it; a visit for the client's TeleSales. One per lead.
            var bookings = funnel.Where(f => f.ToStatus == 5).GroupBy(f => (f.LeadId, f.ChangedById)).Select(g => g.Key.ChangedById).ToList();
            var losses = funnel.Where(f => f.ToStatus == 8).GroupBy(f => (f.LeadId, f.ChangedById)).Select(g => g.Key.ChangedById).ToList();
            var visits = funnel.Where(f => f.ToStatus == 6 && f.Owner != null).GroupBy(f => f.LeadId).Select(g => g.First().Owner!.Value).ToList();

            double? AvgReply(IEnumerable<(DateTime? In, DateTime? Out)> rows)
            {
                var mins = rows.Where(r => r.In != null && r.Out != null && r.Out > r.In).Select(r => (r.Out!.Value - r.In!.Value).TotalMinutes).ToList();
                return mins.Count == 0 ? null : Math.Round(mins.Average());
            }

            object TeamRow(Guid? teamId, string name)
            {
                var teamAccounts = accounts.Where(a => teamOfAccount[a.Id] == teamId).ToList();
                var accountIds = teamAccounts.Select(a => a.Id).ToHashSet();
                var members = agents.Where(a => a.ManagerId == teamId).ToList();
                var memberIds = members.Select(m => m.Id).ToHashSet();
                var teamCreated = created.Where(c => accountIds.Contains(c.WhatsAppAccountId)).ToList();
                return new
                {
                    id = teamId,
                    name,
                    numbers = teamAccounts.Select(a => a.Name).ToList(),
                    newChats = teamCreated.Count,
                    newAssigned = teamCreated.Count(c => c.AssignedSalesAgentId != null),
                    activeChats = active.Count(c => accountIds.Contains(c.WhatsAppAccountId)),
                    bookings = bookings.Count(b => memberIds.Contains(b)),
                    visits = visits.Count(v => memberIds.Contains(v)),
                    lost = losses.Count(l => memberIds.Contains(l)),
                    waitingNow = waiting.Count(w => w.PendingTeamManagerId == teamId && teamId != null
                                                 || w.PendingTeamManagerId == null && accountIds.Contains(w.WhatsAppAccountId)),
                    unreadNow = unread.Count(u => memberIds.Contains(u.Agent)),
                    avgFirstReplyMinutes = AvgReply(teamCreated.Select(c => (c.FirstIn, c.FirstHuman))),
                    members = members
                        .Select(m => new
                        {
                            m.Id,
                            m.Name,
                            m.IsActive,
                            newChats = created.Count(c => c.AssignedSalesAgentId == m.Id),
                            activeChats = active.Count(c => c.AssignedSalesAgentId == m.Id),
                            bookings = bookings.Count(b => b == m.Id),
                            visits = visits.Count(v => v == m.Id),
                            lost = losses.Count(l => l == m.Id),
                            unreadNow = unread.Count(u => u.Agent == m.Id),
                            avgFirstReplyMinutes = AvgReply(created.Where(c => c.AssignedSalesAgentId == m.Id).Select(c => (c.FirstIn, c.FirstHuman)))
                        })
                        .Where(m => m.IsActive || CurrentRole == "9" || m.newChats + m.activeChats + m.bookings + m.visits + m.lost > 0)
                        .OrderByDescending(m => m.bookings).ThenByDescending(m => m.activeChats).ThenBy(m => m.Name)
                        .ToList()
                };
            }

            var teams = leaders.Select(l => TeamRow(l.Id, l.FullName)).ToList();
            if (accounts.Any(a => teamOfAccount[a.Id] == null) || agents.Any(a => a.ManagerId == null))
                teams.Add(TeamRow(null, "أرقام بدون فريق"));

            return Ok(new
            {
                from = fromDay.ToString("yyyy-MM-dd"),
                to = toDay.ToString("yyyy-MM-dd"),
                today = today.ToString("yyyy-MM-dd"),
                totals = new
                {
                    newChats = created.Count,
                    newAssigned = created.Count(c => c.AssignedSalesAgentId != null),
                    activeChats = active.Count,
                    bookings = bookings.Count,
                    visits = visits.Count,
                    lost = losses.Count,
                    waitingNow = waiting.Count,
                    unreadNow = unread.Count,
                    avgFirstReplyMinutes = AvgReply(created.Select(c => (c.FirstIn, c.FirstHuman)))
                },
                teams
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
