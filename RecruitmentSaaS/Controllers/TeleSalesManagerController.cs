using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Services;
using System.Security.Claims;

namespace RecruitmentSaaS.Controllers
{
    [Authorize(Roles = "7,8")] // 7 = team leader, 8 = TeleSales manager — both see one team
    public class TeleSalesManagerController : Controller
    {
        private readonly RecruitmentCrmContext _context;
        private readonly INotificationService _notifications;

        public TeleSalesManagerController(RecruitmentCrmContext context, INotificationService notifications)
        {
            _context = context;
            _notifications = notifications;
        }

        private Guid CurrentUserId =>
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private string CurrentUserName =>
            User.FindFirstValue(ClaimTypes.Name) ?? "مدير تيلي سيلز";

        private bool IsHead => User.IsInRole("8");

        // The team leader whose team this page is about: me, or for the TeleSales manager their team leader
        private Guid? TeamLeaderId()
        {
            if (!IsHead) return CurrentUserId;
            var me = CurrentUserId;
            return _context.Users.Where(u => u.Id == me).Select(u => u.ManagerId).FirstOrDefault();
        }

        // The team's TeleSales (and its TeleSales manager); empty for a TeleSales manager with no team
        private IQueryable<Models.Entities.User> TeamMembersQuery()
        {
            var leader = TeamLeaderId();
            var me = CurrentUserId;
            return leader == null
                ? _context.Users.Where(u => u.Id == me)
                : _context.Users.Where(u => u.ManagerId == leader && (u.Role == 3 || u.Role == 8));
        }

        // ── GET /TeleSalesManager/Index ──────────────────────────────────────
        public async Task<IActionResult> Index(int? month, int? year)
        {
            var now = DateTime.UtcNow;
            var selMonth = month ?? now.Month;
            var selYear = year ?? now.Year;

            var curStart = new DateTime(selYear, selMonth, 1, 0, 0, 0, DateTimeKind.Utc);
            var curEnd = curStart.AddMonths(1);

            // Previous month boundaries
            var prevStart = curStart.AddMonths(-1);
            var prevEnd = curStart;

            // My TeleSales team (users with ManagerId = me)
            var myTeam = await TeamMembersQuery()
                .Where(u => u.IsActive)
                .OrderBy(u => u.FullName)
                .ToListAsync();

            var myTeamIds = myTeam.Select(u => u.Id).ToList();

            if (!myTeamIds.Any())
            {
                ViewBag.MyTeam = myTeam;
                ViewBag.SelMonth = selMonth;
                ViewBag.SelYear = selYear;
                ViewBag.MonthLabel = curStart.ToString("MMMM yyyy", new System.Globalization.CultureInfo("ar-EG"));
                ViewBag.PrevMonthLabel = prevStart.ToString("MMMM yyyy", new System.Globalization.CultureInfo("ar-EG"));
                ViewBag.ManagerName = CurrentUserName;
                ViewBag.TeamStats = new List<object>();
                ViewBag.TeamStatusBreakdown = new List<object>();
                // A team with no TeleSales yet: every number is zero (the page reads all of them)
                ViewBag.TotalLeads = 0;
                ViewBag.TotalConverted = 0;
                ViewBag.TotalRate = 0d;
                ViewBag.PrevTotalLeads = 0;
                ViewBag.PrevTotalConverted = 0;
                ViewBag.PrevTotalRate = 0d;
                ViewBag.LeadsTrend = 0;
                ViewBag.ConvTrend = 0;
                ViewBag.RateTrend = 0d;
                return View();
            }

            // ── All leads assigned to my team THIS month ─────────────────────
            var allLeadsCur = await _context.Leads
                .Where(l => myTeamIds.Contains(l.AssignedSalesId ?? Guid.Empty)
                         && l.CreatedAt >= curStart && l.CreatedAt < curEnd)
                .Select(l => new
                {
                    l.AssignedSalesId,
                    l.Status,
                    l.IsConverted,
                    l.CreatedAt
                })
                .ToListAsync();

            // ── All leads assigned to my team PREVIOUS month ─────────────────
            var allLeadsPrev = await _context.Leads
                .Where(l => myTeamIds.Contains(l.AssignedSalesId ?? Guid.Empty)
                         && l.CreatedAt >= prevStart && l.CreatedAt < prevEnd)
                .Select(l => new { l.AssignedSalesId, l.IsConverted })
                .ToListAsync();

            // ── Build per-member stats ────────────────────────────────────────
            var statusLabels = new[] { "", "عميل جديد", "تم التواصل", "مهتم", "يفكر", "وعد بالزيارة", "زيارة", "تحويل", "ملغى" };

            var teamStats = myTeam.Select(member =>
            {
                var curLeads = allLeadsCur.Where(l => l.AssignedSalesId == member.Id).ToList();
                var prevLeads = allLeadsPrev.Where(l => l.AssignedSalesId == member.Id).ToList();

                var curTotal = curLeads.Count;
                var curConverted = curLeads.Count(l => l.IsConverted);
                var prevTotal = prevLeads.Count;
                var prevConverted = prevLeads.Count(l => l.IsConverted);

                double curRate = curTotal > 0 ? Math.Round((double)curConverted / curTotal * 100, 1) : 0;
                double prevRate = prevTotal > 0 ? Math.Round((double)prevConverted / prevTotal * 100, 1) : 0;

                var byStatus = curLeads
                    .GroupBy(l => l.Status)
                    .Select(g => new { Status = (int)g.Key, Count = g.Count() })
                    .OrderBy(g => g.Status)
                    .ToList();

                return new
                {
                    MemberId = (Guid)member.Id,
                    MemberName = member.FullName,
                    MemberEmail = member.Email,

                    // This month
                    CurTotal = curTotal,
                    CurConverted = curConverted,
                    CurRate = curRate,
                    ByStatus = byStatus,

                    // Previous month
                    PrevTotal = prevTotal,
                    PrevConverted = prevConverted,
                    PrevRate = prevRate,

                    // Trends
                    LeadsTrend = curTotal - prevTotal,
                    ConvTrend = curConverted - prevConverted,
                    RateTrend = Math.Round(curRate - prevRate, 1)
                };
            })
            .OrderByDescending(s => s.CurTotal)
            .ToList();

            // ── Team totals ──────────────────────────────────────────────────
            int totalLeadsCur = allLeadsCur.Count;
            int totalConvCur = allLeadsCur.Count(l => l.IsConverted);
            int totalLeadsPrev = allLeadsPrev.Count;
            int totalConvPrev = allLeadsPrev.Count(l => l.IsConverted);

            double totalRateCur = totalLeadsCur > 0 ? Math.Round((double)totalConvCur / totalLeadsCur * 100, 1) : 0;
            double totalRatePrev = totalLeadsPrev > 0 ? Math.Round((double)totalConvPrev / totalLeadsPrev * 100, 1) : 0;

            // ── Status breakdown for whole team ──────────────────────────────
            var teamStatusBreakdown = allLeadsCur
                .GroupBy(l => l.Status)
                .Select(g => new { Status = (int)g.Key, Count = g.Count() })
                .OrderBy(g => g.Status)
                .ToList();

            // ── ViewBag ──────────────────────────────────────────────────────
            ViewBag.MyTeam = myTeam;
            ViewBag.TeamStats = teamStats;
            ViewBag.StatusLabels = statusLabels;
            ViewBag.SelMonth = selMonth;
            ViewBag.SelYear = selYear;
            ViewBag.MonthLabel = curStart.ToString("MMMM yyyy", new System.Globalization.CultureInfo("ar-EG"));
            ViewBag.PrevMonthLabel = prevStart.ToString("MMMM yyyy", new System.Globalization.CultureInfo("ar-EG"));

            ViewBag.TotalLeads = totalLeadsCur;
            ViewBag.TotalConverted = totalConvCur;
            ViewBag.TotalRate = totalRateCur;
            ViewBag.PrevTotalLeads = totalLeadsPrev;
            ViewBag.PrevTotalConverted = totalConvPrev;
            ViewBag.PrevTotalRate = totalRatePrev;
            ViewBag.LeadsTrend = totalLeadsCur - totalLeadsPrev;
            ViewBag.ConvTrend = totalConvCur - totalConvPrev;
            ViewBag.RateTrend = Math.Round(totalRateCur - totalRatePrev, 1);
            ViewBag.TeamStatusBreakdown = teamStatusBreakdown;
            ViewBag.ManagerName = CurrentUserName;

            return View();
        }

        // ── GET /TeleSalesManager/MemberLeads/{userId} ───────────────────────
        // Drill-down: all leads for a specific TeleSales this month
        public async Task<IActionResult> MemberLeads(Guid userId, int? month, int? year)
        {
            var now = DateTime.UtcNow;
            var selMonth = month ?? now.Month;
            var selYear = year ?? now.Year;
            var curStart = new DateTime(selYear, selMonth, 1, 0, 0, 0, DateTimeKind.Utc);
            var curEnd = curStart.AddMonths(1);

            // Verify this TeleSales belongs to me
            var member = await TeamMembersQuery()
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (member == null) return Forbid();

            var leads = await _context.Leads
                .Include(l => l.Campaign)
                .Where(l => l.AssignedSalesId == userId
                          && l.CreatedAt >= curStart
                          && l.CreatedAt < curEnd)
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();

            ViewBag.Member = member;
            ViewBag.SelMonth = selMonth;
            ViewBag.SelYear = selYear;
            ViewBag.MonthLabel = curStart.ToString("MMMM yyyy", new System.Globalization.CultureInfo("ar-EG"));

            return View(leads);
        }

        // ── GET /TeleSalesManager/TeamFormLeads ──────────────────────────────
        // All of my team's leads: everything assigned to my TeleSales (any source) plus my team
        // form's leads still waiting for me. member = <userId> | "unassigned"; status; q; page.
        public async Task<IActionResult> TeamFormLeads(string? member, byte? status, string? q, int? minAge, int? maxAge, int page = 1)
        {
            const int pageSize = 50;
            var leaderId = TeamLeaderId();
            if (!IsHead)
                await TeamLeadForms.EnsureForManagersAsync(_context, new[] { CurrentUserId });

            var form = await _context.TeamLeadForms.AsNoTracking()
                .FirstOrDefaultAsync(f => f.ManagerId == leaderId);

            // Include deactivated members: their leads are still the team's
            var team = await TeamMembersQuery().AsNoTracking()
                .Include(u => u.Manager)
                .OrderByDescending(u => u.IsActive).ThenBy(u => u.FullName)
                .ToListAsync();
            var teamIds = team.Select(u => u.Id).ToList();

            var scope = TeamLeadsScope(teamIds);

            // Header counters + per-member counts (whole team, before filters)
            ViewBag.TotalCount = await scope.CountAsync();
            ViewBag.UnassignedCount = await scope.CountAsync(l => l.AssignedSalesId == null);
            ViewBag.ConvertedCount = await scope.CountAsync(l => l.IsConverted);
            ViewBag.MemberCounts = await scope.Where(l => l.AssignedSalesId != null)
                .GroupBy(l => l.AssignedSalesId!.Value)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count);

            var query = scope;
            if (member == "unassigned")
                query = query.Where(l => l.AssignedSalesId == null);
            else if (Guid.TryParse(member, out var memberId))
                query = query.Where(l => l.AssignedSalesId == memberId);

            if (status.HasValue)
                query = query.Where(l => l.Status == status.Value);

            // Age range (leads without an age are left out once a range is set)
            if (minAge.HasValue)
                query = query.Where(l => l.Age != null && l.Age >= minAge.Value);
            if (maxAge.HasValue)
                query = query.Where(l => l.Age != null && l.Age <= maxAge.Value);

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                var phone = PhoneNumbers.Normalize(term);
                query = query.Where(l => l.FullName.Contains(term)
                                      || (l.LeadCode != null && l.LeadCode.Contains(term))
                                      || l.Phone.Contains(term)
                                      || (phone.Length >= 4 && l.Phone.Contains(phone)));
            }

            var filteredCount = await query.CountAsync();
            page = Math.Max(1, page);
            var leads = await query
                .Include(l => l.AssignedSales)
                .OrderByDescending(l => l.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.FormUrl = form == null ? null : $"{Request.Scheme}://{Request.Host}/register/{form.Slug}";
            ViewBag.Team = team;
            ViewBag.MyTeam = team.Where(u => u.IsActive).ToList(); // who can receive leads
            ViewBag.Member = member;
            ViewBag.Status = status;
            ViewBag.Q = q;
            ViewBag.MinAge = minAge;
            ViewBag.MaxAge = maxAge;
            ViewBag.Page = page;
            ViewBag.PageCount = (int)Math.Ceiling(filteredCount / (double)pageSize);
            ViewBag.FilteredCount = filteredCount;
            ViewBag.IsHead = IsHead;

            return View(leads);
        }

        // A lead belongs to the team when its team form brought it in, or one of its TeleSales holds it
        private IQueryable<Models.Entities.Lead> TeamLeadsScope(List<Guid> teamIds)
        {
            var leader = TeamLeaderId();
            return _context.Leads.Where(l => (leader != null && l.TeamManagerId == leader)
                                          || (l.AssignedSalesId != null && teamIds.Contains(l.AssignedSalesId.Value)));
        }

        // ── POST /TeleSalesManager/AssignTeamLead ────────────────────────────
        // Assign a waiting lead, or move a lead between members of my team. The lead's WhatsApp
        // chat moves with it so the new person sees the conversation.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignTeamLead(Guid leadId, Guid salesUserId, string? returnQuery)
        {
            IActionResult Back() => Redirect("/TeleSalesManager/TeamFormLeads" +
                (!string.IsNullOrEmpty(returnQuery) && returnQuery.StartsWith('?') ? returnQuery : ""));

            var teamIds = await TeamMembersQuery()
                .Select(u => u.Id)
                .ToListAsync();

            var lead = await TeamLeadsScope(teamIds)
                .Include(l => l.AssignedSales)
                .FirstOrDefaultAsync(l => l.Id == leadId);

            var member = await TeamMembersQuery()
                .FirstOrDefaultAsync(u => u.Id == salesUserId && u.IsActive);

            if (lead == null || member == null)
            {
                TempData["Error"] = "هذا العميل غير متاح أو الموظف ليس في فريقك";
                return Back();
            }
            if (lead.AssignedSalesId == member.Id)
                return Back();

            var previous = lead.AssignedSales;
            lead.AssignedSalesId = member.Id;
            lead.UpdatedAt = DateTime.UtcNow;

            // The customer's WhatsApp chat follows the lead (unless someone else deliberately holds it).
            // previous is null for a lead that was waiting for assignment.
            var previousId = previous?.Id;
            var chats = await _context.WhatsAppConversations
                .Where(c => c.LeadId == lead.Id
                         && (c.AssignedSalesAgentId == null || c.AssignedSalesAgentId == previousId))
                .ToListAsync();
            foreach (var chat in chats)
            {
                chat.AssignedSalesAgentId = member.Id;
                chat.UpdatedAt = DateTime.UtcNow;
            }

            _context.LeadActivities.Add(new Models.Entities.LeadActivity
            {
                Id = Guid.NewGuid(),
                LeadId = lead.Id,
                ActivityType = 8,
                Description = previous == null
                    ? $"تم تعيين العميل لـ {member.FullName} بواسطة {CurrentUserName}"
                    : $"تم نقل العميل من {previous.FullName} إلى {member.FullName} بواسطة {CurrentUserName}",
                CreatedById = CurrentUserId,
                CreatedByName = CurrentUserName,
                ActorType = 1,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            try
            {
                await _notifications.SendAsync(member.Id, "عميل جديد من مدير الفريق",
                    $"{lead.FullName} — {lead.Phone}", link: $"/TeleSales/LeadDetail/{lead.Id}");
            }
            catch { /* the assignment is saved; a failed notification must not undo it */ }

            TempData["Success"] = previous == null
                ? $"تم تعيين {lead.FullName} لـ {member.FullName}"
                : $"تم نقل {lead.FullName} من {previous.FullName} إلى {member.FullName}"
                  + (chats.Count > 0 ? " (ومعه محادثة الواتساب)" : "");
            return Back();
        }
    }
}