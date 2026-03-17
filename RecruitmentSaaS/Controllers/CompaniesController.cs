using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using System.Security.Claims;

namespace RecruitmentSaaS.Controllers
{
    [Authorize(Roles = "1")]
    public class CompaniesController : Controller
    {
        private readonly RecruitmentCrmContext _context;

        public CompaniesController(RecruitmentCrmContext context)
        {
            _context = context;
        }

        private Guid CurrentUserId =>
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // ── GET /Companies/Index ──────────────────────────────────────────────
        public async Task<IActionResult> Index(string? country, string? field, bool? active)
        {
            var query = _context.Companies
                .Include(c => c.CreatedBy)
                .Include(c => c.CompanyJobs)
                .AsQueryable();

            if (!string.IsNullOrEmpty(country))
                query = query.Where(c => c.Country == country);

            if (!string.IsNullOrEmpty(field))
                query = query.Where(c => c.CompanyJobs.Any(j => j.JobTitle.Contains(field)));

            if (active.HasValue)
                query = query.Where(c => c.IsActive == active.Value);

            var companies = await query
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new
                {
                    c.Id,
                    c.Name,
                    c.Country,
                    c.City,
                    c.ContactPhone,
                    c.ContactEmail,
                    c.ContractStartDate,
                    c.IsActive,
                    c.Notes,
                    c.CreatedAt,
                    CreatedByName = c.CreatedBy.FullName,
                    // Total requested = sum of all jobs
                    RequestedCount = c.CompanyJobs.Where(j => j.IsActive).Sum(j => (int?)j.RequestedCount) ?? 0,
                    // Jobs list for display
                    Jobs = c.CompanyJobs.Where(j => j.IsActive)
                        .Select(j => new { j.Id, j.JobTitle, j.RequestedCount }).ToList(),
                    // Count assigned candidates
                    AssignedCount = _context.Candidates.Count(cand => cand.CompanyId == c.Id),
                    CompletedCount = _context.Candidates.Count(cand => cand.CompanyId == c.Id && cand.IsCompleted)
                })
                .ToListAsync();

            // Filters for dropdowns
            ViewBag.Countries = await _context.Companies
                .Select(c => c.Country).Distinct().OrderBy(c => c).ToListAsync();
            ViewBag.Fields = await _context.CompanyJobs
                .Select(j => j.JobTitle).Distinct().OrderBy(f => f).ToListAsync();

            ViewBag.Companies = companies;
            ViewBag.FilterCountry = country;
            ViewBag.FilterField = field;
            ViewBag.FilterActive = active;

            return View();
        }

        // ── GET /Companies/Detail/{id} ────────────────────────────────────────
        public async Task<IActionResult> Detail(Guid id)
        {
            var company = await _context.Companies
                .Include(c => c.CreatedBy)
                .Include(c => c.CompanyJobs.Where(j => j.IsActive))
                .FirstOrDefaultAsync(c => c.Id == id);

            if (company == null) return NotFound();

            var candidates = await _context.Candidates
                .Where(c => c.CompanyId == id)
                .Include(c => c.CurrentPackageStage)
                .Include(c => c.JobPackage)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new
                {
                    c.Id,
                    c.FullName,
                    c.Phone,
                    c.IsCompleted,
                    c.TotalPaidEgp,
                    PackageName = c.JobPackage.Name,
                    CurrentStageName = c.CurrentPackageStage != null
                        ? c.CurrentPackageStage.StageName : "—"
                })
                .ToListAsync();

            ViewBag.Company = company;
            ViewBag.Candidates = candidates;
            ViewBag.AssignedCount = candidates.Count;
            ViewBag.CompletedCount = candidates.Count(c => c.IsCompleted);
            int totalRequested = company.CompanyJobs?.Where(j => j.IsActive).Sum(j => j.RequestedCount) ?? 0;
            ViewBag.RequestedCount = totalRequested;
            ViewBag.RemainingCount = totalRequested - candidates.Count;

            return View();
        }

        // ── POST /Companies/Create ────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            string name, string country, string? city,
            string? contactPhone, string? contactEmail,
            DateOnly? contractStartDate, string? notes,
            [FromForm] List<string> jobTitles,
            [FromForm] List<int> jobCounts)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(country))
            {
                TempData["Error"] = "الاسم والدولة مطلوبة";
                return RedirectToAction("Index");
            }

            var companyId = Guid.NewGuid();
            var company = new Company
            {
                Id = companyId,
                Name = name.Trim(),
                Country = country.Trim(),
                City = city?.Trim(),
                ContactPhone = contactPhone?.Trim(),
                ContactEmail = contactEmail?.Trim(),
                ContractStartDate = contractStartDate,
                Notes = notes?.Trim(),
                IsActive = true,
                CreatedById = CurrentUserId,
                CreatedAt = DateTime.UtcNow
            };
            _context.Companies.Add(company);

            // Add job titles
            for (int i = 0; i < jobTitles.Count; i++)
            {
                var title = jobTitles[i]?.Trim();
                if (string.IsNullOrEmpty(title)) continue;
                var count = (i < jobCounts.Count && jobCounts[i] > 0) ? jobCounts[i] : 1;
                _context.CompanyJobs.Add(new CompanyJob
                {
                    Id = Guid.NewGuid(),
                    CompanyId = companyId,
                    JobTitle = title,
                    RequestedCount = count,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();
            TempData["Success"] = $"تم إضافة شركة {name} بنجاح";
            return RedirectToAction("Index");
        }

        // ── POST /Companies/Edit ──────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            Guid id, string name, string country, string? city,
            string? contactPhone, string? contactEmail,
            DateOnly? contractStartDate, string? notes, bool isActive)
        {
            var company = await _context.Companies.FindAsync(id);
            if (company == null) return NotFound();

            company.Name = name.Trim();
            company.Country = country.Trim();
            company.City = city?.Trim();
            company.ContactPhone = contactPhone?.Trim();
            company.ContactEmail = contactEmail?.Trim();
            company.ContractStartDate = contractStartDate;
            company.Notes = notes?.Trim();
            company.IsActive = isActive;
            company.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            TempData["Success"] = "تم تحديث بيانات الشركة بنجاح";
            return RedirectToAction("Index");
        }

        // ── POST /Companies/AddJob ─────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddJob(Guid companyId, string jobTitle, int requestedCount, string? notes)
        {
            if (string.IsNullOrWhiteSpace(jobTitle))
            {
                TempData["Error"] = "المسمى الوظيفي مطلوب";
                return RedirectToAction("Detail", new { id = companyId });
            }
            _context.CompanyJobs.Add(new CompanyJob
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                JobTitle = jobTitle.Trim(),
                RequestedCount = requestedCount > 0 ? requestedCount : 1,
                Notes = notes?.Trim(),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            TempData["Success"] = $"تم إضافة {jobTitle} بنجاح";
            return RedirectToAction("Detail", new { id = companyId });
        }

        // ── POST /Companies/EditJob ────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditJob(Guid jobId, Guid companyId, string jobTitle, int requestedCount, string? notes)
        {
            var job = await _context.CompanyJobs.FindAsync(jobId);
            if (job == null) return NotFound();

            job.JobTitle = jobTitle.Trim();
            job.RequestedCount = requestedCount > 0 ? requestedCount : 1;
            job.Notes = notes?.Trim();
            await _context.SaveChangesAsync();
            TempData["Success"] = "تم تحديث المسمى الوظيفي";
            return RedirectToAction("Detail", new { id = companyId });
        }

        // ── POST /Companies/DeleteJob ──────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteJob(Guid jobId, Guid companyId)
        {
            var job = await _context.CompanyJobs.FindAsync(jobId);
            if (job != null)
            {
                _context.CompanyJobs.Remove(job);
                await _context.SaveChangesAsync();
            }
            TempData["Success"] = "تم حذف المسمى الوظيفي";
            return RedirectToAction("Detail", new { id = companyId });
        }

        // ── POST /Companies/AssignCandidate ───────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignCandidate(Guid companyId, Guid candidateId)
        {
            var candidate = await _context.Candidates.FindAsync(candidateId);
            if (candidate == null) return NotFound();

            candidate.CompanyId = companyId;
            candidate.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            TempData["Success"] = "تم ربط المرشح بالشركة";
            return RedirectToAction("Detail", new { id = companyId });
        }

        // ── POST /Companies/UnassignCandidate ─────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UnassignCandidate(Guid companyId, Guid candidateId)
        {
            var candidate = await _context.Candidates.FindAsync(candidateId);
            if (candidate == null) return NotFound();

            candidate.CompanyId = null;
            candidate.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            TempData["Success"] = "تم إلغاء ربط المرشح بالشركة";
            return RedirectToAction("Detail", new { id = companyId });
        }

        // ── GET /Companies/SearchCandidates (AJAX) ────────────────────────────
        [HttpGet]
        public async Task<IActionResult> SearchCandidates(string q)
        {
            if (string.IsNullOrWhiteSpace(q))
                return Json(new List<object>());

            var results = await _context.Candidates
                .Include(c => c.JobPackage)
                .Where(c => c.CompanyId == null &&
                    (c.FullName.Contains(q) || c.Phone.Contains(q) ||
                     (c.PassportNumber != null && c.PassportNumber.Contains(q))))
                .OrderBy(c => c.FullName)
                .Take(10)
                .Select(c => new
                {
                    c.Id,
                    c.FullName,
                    c.Phone,
                    c.PassportNumber,
                    PackageName = c.JobPackage.Name
                })
                .ToListAsync();

            return Json(results);
        }
    }
}