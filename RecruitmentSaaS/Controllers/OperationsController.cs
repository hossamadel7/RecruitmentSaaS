using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using System.Security.Claims;

namespace RecruitmentSaaS.Controllers
{
    [Authorize(Roles = "5")]
    public class OperationsController : Controller
    {
        private readonly RecruitmentCrmContext _context;

        public OperationsController(RecruitmentCrmContext context)
        {
            _context = context;
        }

        private Guid CurrentUserId =>
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // ── GET /Operations/Index ─────────────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            var totalCandidates = await _context.Candidates.CountAsync();

            var inProgress = await _context.Candidates
                .CountAsync(c => c.IsCompleted != true);

            var completedCount = await _context.Candidates
                .CountAsync(c => c.IsCompleted == true);

            // Candidates by stage
            var byStage = await _context.Candidates
                .Where(c => c.IsCompleted != true)
                .GroupBy(c => c.CurrentStage)
                .Select(g => new { Stage = g.Key, Count = g.Count() })
                .OrderBy(g => g.Stage)
                .ToListAsync();

            ViewBag.TotalCandidates = totalCandidates;
            ViewBag.InProgress      = inProgress;
            ViewBag.CompletedCount  = completedCount;
            ViewBag.ByStage         = byStage;

            return View();
        }

        // ── GET /Operations/Candidates ────────────────────────────────────────
        public async Task<IActionResult> Candidates(string? q, int? stage, int page = 1)
        {
            const int pageSize = 20;

            var query = _context.Candidates
                .Include(c => c.JobPackage)
                .Include(c => c.AssignedSales)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(c =>
                    c.FullName.Contains(q) ||
                    c.Phone.Contains(q) ||
                    (c.NationalId != null && c.NationalId.Contains(q)));

            if (stage.HasValue)
                query = query.Where(c => c.CurrentStage == stage.Value);

            query = query.OrderByDescending(c => c.CreatedAt);

            var total = await query.CountAsync();

            var candidates = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.Q          = q;
            ViewBag.Stage      = stage;
            ViewBag.Page       = page;
            ViewBag.Total      = total;
            ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);

            return View(candidates);
        }
    }
}
