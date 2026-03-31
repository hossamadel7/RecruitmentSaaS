using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using System.Security.Claims;

namespace RecruitmentSaaS.Controllers
{
    [Authorize(Roles = "7")]
    public class TeleSalesManagerController : Controller
    {
        private readonly RecruitmentCrmContext _context;

        public TeleSalesManagerController(RecruitmentCrmContext context)
        {
            _context = context;
        }

        private Guid CurrentUserId =>
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private string CurrentUserName =>
            User.FindFirstValue(ClaimTypes.Name) ?? "مدير تيلي سيلز";

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
            var myTeam = await _context.Users
                .Where(u => u.ManagerId == CurrentUserId && u.Role == 3 && u.IsActive)
                .OrderBy(u => u.FullName)
                .ToListAsync();

            var myTeamIds = myTeam.Select(u => u.Id).ToList();

            if (!myTeamIds.Any())
            {
                ViewBag.MyTeam = myTeam;
                ViewBag.SelMonth = selMonth;
                ViewBag.SelYear = selYear;
                ViewBag.MonthLabel = curStart.ToString("MMMM yyyy", new System.Globalization.CultureInfo("ar-EG"));
                ViewBag.TeamStats = new List<object>();
                ViewBag.TotalLeads = 0;
                ViewBag.TotalConverted = 0;
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
            var member = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == userId
                                       && u.ManagerId == CurrentUserId
                                       && u.Role == 3);

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
    }
}