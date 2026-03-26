using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.Security.Claims;

namespace RecruitmentSaaS.Controllers
{
    [Authorize(Roles = "1,3")]
    public class AdminGoogleSheetsController : Controller
    {
        private readonly RecruitmentCrmContext _context;
        private readonly IGoogleSheetsLeadImportService _importService;

        public AdminGoogleSheetsController(
            RecruitmentCrmContext context,
            IGoogleSheetsLeadImportService importService)
        {
            _context = context;
            _importService = importService;
        }

        // ── GET /AdminGoogleSheets/Index ──────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            var sheets = await _context.SalesGoogleSheets
                .Include(s => s.Campaign)
                .Include(s => s.SalesGoogleSheetUsers)
                    .ThenInclude(su => su.SalesUser)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();

            return View(sheets);
        }

        // ── GET /AdminGoogleSheets/Create ─────────────────────────────────────
        public async Task<IActionResult> Create()
        {
            await PopulateViewBag();
            return View();
        }

        // ── POST /AdminGoogleSheets/Create ────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            string name,
            string spreadsheetId,
            string sheetName,
            Guid? campaignId,
            List<Guid> salesUserIds)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(spreadsheetId))
            {
                TempData["Error"] = "الاسم ورابط الشيت مطلوبان";
                await PopulateViewBag();
                return View();
            }

            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var sheet = new SalesGoogleSheet
            {
                Id = Guid.NewGuid(),
                Name = name.Trim(),
                SpreadsheetId = spreadsheetId.Trim(),
                SheetName = string.IsNullOrWhiteSpace(sheetName) ? "Sheet1" : sheetName.Trim(),
                CampaignId = campaignId,
                IsActive = true,
                CreatedById = userId,
                CreatedAt = DateTime.UtcNow,
            };

            _context.SalesGoogleSheets.Add(sheet);

            foreach (var salesId in salesUserIds.Distinct())
            {
                _context.SalesGoogleSheetUsers.Add(new SalesGoogleSheetUser
                {
                    Id = Guid.NewGuid(),
                    SheetId = sheet.Id,
                    SalesUserId = salesId,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                });
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم إضافة الشيت \"{sheet.Name}\" بنجاح";
            return RedirectToAction(nameof(Index));
        }

        // ── POST /AdminGoogleSheets/ToggleActive ──────────────────────────────
        [HttpPost]
        public async Task<IActionResult> ToggleActive(Guid id)
        {
            var sheet = await _context.SalesGoogleSheets.FindAsync(id);
            if (sheet == null) return NotFound();

            sheet.IsActive = !sheet.IsActive;
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // ── POST /AdminGoogleSheets/Delete ────────────────────────────────────
        [HttpPost]
        [Authorize(Roles = "1")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var sheet = await _context.SalesGoogleSheets
                .Include(s => s.SalesGoogleSheetUsers)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (sheet == null) return NotFound();

            // Detach leads but do not delete them
            var leads = await _context.Leads
                .Where(l => l.GoogleSheetId == id)
                .ToListAsync();

            foreach (var lead in leads)
                lead.GoogleSheetId = null;

            _context.SalesGoogleSheetUsers.RemoveRange(sheet.SalesGoogleSheetUsers);
            _context.SalesGoogleSheets.Remove(sheet);

            await _context.SaveChangesAsync();

            TempData["Success"] = "تم حذف الشيت بنجاح";
            return RedirectToAction(nameof(Index));
        }

        // ── POST /AdminGoogleSheets/RunImport ─────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> RunImport(Guid id)
        {
            var result = await _importService.ImportSheetAsync(id);

            TempData["ImportResult_Imported"] = result.Imported;
            TempData["ImportResult_Skipped"] = result.Skipped;
            TempData["ImportResult_Duplicates"] = result.Duplicates;
            TempData["ImportResult_Errors"] = string.Join("\n", result.Errors);

            return RedirectToAction(nameof(ImportResult), new { id });
        }

        // ── GET /AdminGoogleSheets/ImportResult ───────────────────────────────
        public async Task<IActionResult> ImportResult(Guid id)
        {
            var sheet = await _context.SalesGoogleSheets
                .Include(s => s.Campaign)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (sheet == null) return NotFound();

            ViewBag.Sheet = sheet;
            ViewBag.Imported = TempData["ImportResult_Imported"] ?? 0;
            ViewBag.Skipped = TempData["ImportResult_Skipped"] ?? 0;
            ViewBag.Duplicates = TempData["ImportResult_Duplicates"] ?? 0;
            ViewBag.Errors = TempData["ImportResult_Errors"] ?? "";

            return View();
        }

        // ── POST /AdminGoogleSheets/ResetRow ──────────────────────────────────
        [HttpPost]
        [Authorize(Roles = "1")]
        public async Task<IActionResult> ResetRow(Guid id)
        {
            var sheet = await _context.SalesGoogleSheets.FindAsync(id);
            if (sheet == null) return NotFound();

            sheet.LastImportedRow = 1;
            sheet.TotalImported = 0;
            sheet.LastImportedAt = null;
            await _context.SaveChangesAsync();

            TempData["Success"] = "تم إعادة تعيين الشيت — سيتم استيراد كل الصفوف في المرة القادمة";
            return RedirectToAction(nameof(Index));
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private async Task PopulateViewBag()
        {
            ViewBag.Campaigns = new SelectList(
                await _context.Campaigns
                    .Where(c => c.Status == 1)
                    .OrderBy(c => c.Name)
                    .ToListAsync(),
                "Id", "Name");

            // Role 3 = TeleSales, Role 1 = Admin
            ViewBag.SalesUsers = await _context.Users
                .Where(u => u.Role == 3 || u.Role == 1)
                .OrderBy(u => u.FullName)
                .ToListAsync();
        }
    }
}