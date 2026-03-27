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
        public async Task<IActionResult> Index([FromQuery(Name = "ref")] string? salesRef)
        {
            // لو في ref — نحتفظ بيه في الـ Cookie عشان الـ SubmitLead يقراه
            if (!string.IsNullOrWhiteSpace(salesRef)
                && Guid.TryParse(salesRef, out var salesId))
            {
                Response.Cookies.Append("sales_ref", salesId.ToString(), new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Lax,
                    Expires = DateTimeOffset.UtcNow.AddHours(2)
                });
            }

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
            string? interestedJobTitle,
            string? interestedCountry,
            string? notes)
        {
            // 1. Find first active branch
            var activeBranches = await _context.Branches
                .Where(b => b.IsActive)
                .OrderBy(b => b.Name)
                .ToListAsync();

            var resolvedBranchId = activeBranches.Count > 0
                ? activeBranches[0].Id
                : (Guid?)null;

            if (resolvedBranchId == null)
            {
                TempData["FormError"] = "لا يوجد فرع متاح";
                return RedirectToAction("Index");
            }

            // 2. Determine RegisteredById (first admin)
            var systemUserId = await _context.Users
                .Where(u => u.Role == 1 && u.IsActive)
                .OrderBy(u => u.CreatedAt)
                .Select(u => (Guid?)u.Id)
                .FirstOrDefaultAsync();

            if (systemUserId == null)
            {
                TempData["FormError"] = "حدث خطأ في النظام";
                return RedirectToAction("Index");
            }

            // 3. Duplicate phone — silent success
            phone = phone?.Trim() ?? "";
            if (await _context.Leads.AnyAsync(l => l.Phone == phone))
                return RedirectToAction("ThankYou");

            // 4. جيب الـ TeleSales ID من الـ Cookie لو موجود
            Guid? assignedSalesId = null;
            if (Request.Cookies.TryGetValue("sales_ref", out var refCookie)
                && Guid.TryParse(refCookie, out var refSalesId))
            {
                // تحقق إن الـ User ده موجود وـ Role = 3 (TeleSales)
                var salesExists = await _context.Users
                    .AnyAsync(u => u.Id == refSalesId
                               && u.Role == 3
                               && u.IsActive);

                if (salesExists)
                    assignedSalesId = refSalesId;

                // امسح الـ Cookie بعد الاستخدام
                Response.Cookies.Delete("sales_ref");
            }

            // 5. Create Lead
            _context.Leads.Add(new Lead
            {
                Id = Guid.NewGuid(),
                BranchId = resolvedBranchId.Value,
                RegisteredById = systemUserId.Value,
                FullName = fullName?.Trim() ?? "",
                Phone = phone,
                LeadSource = 2,  // موقع إلكتروني
                Status = 1,  // New
                InterestedJobTitle = interestedJobTitle?.Trim(),
                InterestedCountry = interestedCountry?.Trim(),
                Notes = notes?.Trim(),
                AssignedSalesId = assignedSalesId, // ← TeleSales — null لو مفيش ref
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            return RedirectToAction("ThankYou");
        }

        // ── GET /Home/ThankYou ────────────────────────────────────────────
        public IActionResult ThankYou() => View();

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error() =>
            View(new RecruitmentSaaS.Models.ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            });
    }
}