using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Services;
using System.Security.Claims;

namespace RecruitmentSaaS.Controllers
{
    /// <summary>
    /// "تقرير الحجوزات": how many office bookings (حجز) each TeleSales makes per day, and how many of them came.
    /// Admin sees every team; a team leader or TeleSales manager sees their own team.
    /// Counted from the lead funnel history: a booking = the lead moved to status 5 by that TeleSales,
    /// a visit = the lead moved to status 6 (by reception or the TeleSales), credited to the lead's TeleSales.
    /// </summary>
    [Authorize(Roles = "1,7,8")]
    public class ReservationsController : Controller
    {
        private const byte Booked = 5, Visited = 6, Converted = 7;
        private const int MaxDays = 62;

        private readonly RecruitmentCrmContext _context;

        public ReservationsController(RecruitmentCrmContext context)
        {
            _context = context;
        }

        private Guid CurrentUserId => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        private bool IsAdmin => User.IsInRole("1");

        public sealed record AgentRow(Guid Id, string Name, string? Team, int Today, int Total, int Came, Dictionary<DateTime, int> PerDay);
        public sealed record Appointment(Guid LeadId, string? LeadCode, string Name, string Phone, DateTime At, string? Agent, byte Status);

        // ── GET /Reservations/Index?from=yyyy-MM-dd&to=yyyy-MM-dd&team={leaderId} ──
        public async Task<IActionResult> Index(DateTime? from, DateTime? to, Guid? team)
        {
            var today = EgyptTime.Today;
            var toDay = (to ?? today).Date;
            var fromDay = (from ?? toDay.AddDays(-6)).Date;
            if (fromDay > toDay) (fromDay, toDay) = (toDay, fromDay);
            if ((toDay - fromDay).TotalDays >= MaxDays) fromDay = toDay.AddDays(-(MaxDays - 1));

            // Whose numbers this page shows
            var agentsQuery = _context.Users.AsNoTracking().Where(u => u.Role == 3 || u.Role == 8);
            List<(Guid Id, string Name)> teams = new();
            if (IsAdmin)
            {
                teams = (await _context.Users.AsNoTracking()
                        .Where(u => u.Role == 7)
                        .OrderBy(u => u.FullName)
                        .Select(u => new { u.Id, u.FullName })
                        .ToListAsync())
                    .Select(t => (t.Id, t.FullName)).ToList();
                if (team != null) agentsQuery = agentsQuery.Where(u => u.ManagerId == team);
            }
            else
            {
                var me = CurrentUserId;
                var leader = User.IsInRole("8")
                    ? await _context.Users.Where(u => u.Id == me).Select(u => u.ManagerId).FirstOrDefaultAsync()
                    : me;
                agentsQuery = leader == null ? agentsQuery.Where(u => u.Id == me) : agentsQuery.Where(u => u.ManagerId == leader);
                team = null;
            }

            var agents = await agentsQuery
                .Select(u => new { u.Id, u.FullName, u.IsActive, Team = u.Manager != null ? u.Manager.FullName : null })
                .ToListAsync();
            var ids = agents.Select(a => a.Id).ToList();

            var fromUtc = EgyptTime.ToUtc(fromDay);
            var toUtc = EgyptTime.ToUtc(toDay.AddDays(1));

            var bookings = await _context.LeadFunnelHistories.AsNoTracking()
                .Where(h => h.ToStatus == Booked && ids.Contains(h.ChangedById) && h.CreatedAt >= fromUtc && h.CreatedAt < toUtc)
                .Select(h => new { h.LeadId, h.ChangedById, h.CreatedAt, LeadStatus = h.Lead.Status })
                .ToListAsync();
            // Re-booking the same customer on the same day counts once
            var booked = bookings
                .Select(b => new { b.LeadId, b.ChangedById, Day = EgyptTime.FromUtc(b.CreatedAt).Date, b.LeadStatus })
                .GroupBy(b => (b.LeadId, b.ChangedById, b.Day)).Select(g => g.First())
                .ToList();

            var rows = agents.Select(a =>
            {
                var mine = booked.Where(b => b.ChangedById == a.Id).ToList();
                var perDay = mine.GroupBy(b => b.Day).ToDictionary(g => g.Key, g => g.Count());
                var came = mine.Select(b => b.LeadId).Distinct()
                    .Count(id => mine.First(b => b.LeadId == id).LeadStatus is Visited or Converted);
                return new AgentRow(a.Id, a.FullName, a.Team, perDay.GetValueOrDefault(today), mine.Count, came, perDay);
            })
            // Inactive TeleSales only appear when they booked something in the period
            .Where(r => agents.First(a => a.Id == r.Id).IsActive || r.Total > 0)
            .OrderByDescending(r => r.Total).ThenBy(r => r.Name)
            .ToList();

            // Visits in the period, whoever recorded them, credited to the lead's TeleSales
            var visits = await _context.LeadFunnelHistories.AsNoTracking()
                .Where(h => h.ToStatus == Visited && h.CreatedAt >= fromUtc && h.CreatedAt < toUtc
                         && h.Lead.AssignedSalesId != null && ids.Contains(h.Lead.AssignedSalesId.Value))
                .Select(h => h.LeadId).Distinct().CountAsync();

            // Office appointments: the coming week, plus booked customers whose day passed without a visit
            var weekEnd = today.AddDays(8);
            var appointments = await _context.Leads.AsNoTracking()
                .Where(l => l.Status == Booked && l.AppointmentDate != null && l.AppointmentDate < weekEnd
                         && l.AppointmentDate >= today.AddDays(-14)
                         && l.AssignedSalesId != null && ids.Contains(l.AssignedSalesId.Value))
                .OrderBy(l => l.AppointmentDate)
                .Select(l => new Appointment(l.Id, l.LeadCode, l.FullName, l.Phone, l.AppointmentDate!.Value,
                                             l.AssignedSales != null ? l.AssignedSales.FullName : null, l.Status))
                .ToListAsync();

            ViewBag.From = fromDay;
            ViewBag.To = toDay;
            ViewBag.Today = today;
            ViewBag.Days = Enumerable.Range(0, (int)(toDay - fromDay).TotalDays + 1).Select(i => fromDay.AddDays(i)).ToList();
            ViewBag.Rows = rows;
            ViewBag.Visits = visits;
            ViewBag.Appointments = appointments;
            ViewBag.IsAdmin = IsAdmin;
            ViewBag.Teams = teams;
            ViewBag.Team = team;
            return View();
        }
    }
}
