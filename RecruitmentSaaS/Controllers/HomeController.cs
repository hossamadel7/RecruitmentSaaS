using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Controllers
{
    [AllowAnonymous]
    public class HomeController : Controller
    {
        private readonly RecruitmentCrmContext _context;
        private readonly ILogger<HomeController> _logger;

        public HomeController(RecruitmentCrmContext context, ILogger<HomeController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // ── GET / ─────────────────────────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            ViewBag.Branches = await _context.Branches
                .Where(b => b.IsActive)
                .OrderBy(b => b.Name)
                .ToListAsync();

            ViewBag.JobPackages = await _context.JobPackages
                .Where(j => j.IsActive)
                .OrderBy(j => j.DestinationCountry)
                .ThenBy(j => j.JobTitle)
                .ToListAsync();

            return View();
        }

        // ── POST /Home/SubmitLead ─────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitLead(
            string fullName,
            string phone,
            Guid? branchId,
            string? interestedJobTitle,
            string? interestedCountry,
            string? notes,
            string? website_trap)   // honeypot — bots fill this, humans don't
        {
            // ── 1. Honeypot: silently redirect bots ───────────────────────
            if (!string.IsNullOrEmpty(website_trap))
                return RedirectToAction("ThankYou");

            // ── 2. Resolve branch ─────────────────────────────────────────
            var activeBranches = await _context.Branches
                .Where(b => b.IsActive)
                .OrderBy(b => b.Name)
                .ToListAsync();

            var resolvedBranchId = activeBranches.Count == 1
                ? activeBranches[0].Id
                : branchId;

            if (resolvedBranchId == null)
            {
                TempData["FormError"] = "يرجى اختيار الفرع";
                return RedirectToAction("Index");
            }

            // ── 3. Resolve RegisteredById — use first admin as system user ─
            var systemUserId = await _context.Users
                .Where(u => u.Role == 1 && u.IsActive)
                .OrderBy(u => u.CreatedAt)
                .Select(u => (Guid?)u.Id)
                .FirstOrDefaultAsync();

            if (systemUserId == null)
            {
                _logger.LogError("Landing page SubmitLead: no active admin user found for RegisteredById.");
                TempData["FormError"] = "حدث خطأ في النظام، يرجى التواصل بنا مباشرة.";
                return RedirectToAction("Index");
            }

            // ── 4. Duplicate phone — silent success (don't leak info) ─────
            phone = (phone ?? "").Trim();
            if (await _context.Leads.AnyAsync(l => l.Phone == phone))
                return RedirectToAction("ThankYou");

            // ── 5. Save ───────────────────────────────────────────────────
            _context.Leads.Add(new Lead
            {
                Id = Guid.NewGuid(),
                BranchId = resolvedBranchId.Value,
                RegisteredById = systemUserId.Value,
                FullName = (fullName ?? "").Trim(),
                Phone = phone,
                LeadSource = 7,   // 7 = موقع إلكتروني
                Status = 1,   // جديد — يدخل بنك التيلي سيلز
                InterestedJobTitle = interestedJobTitle?.Trim(),
                InterestedCountry = interestedCountry?.Trim(),
                Notes = notes?.Trim(),
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            return RedirectToAction("ThankYou");
        }

        // ── GET /Home/ThankYou ────────────────────────────────────────────
        public IActionResult ThankYou() => View();

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error() =>
View(new RecruitmentSaaS.Models.ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
