using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.DTOs;
using RecruitmentSaaS.Models.Entities;
using System.Security.Claims;

namespace RecruitmentSaaS.Controllers
{
    [Authorize(Roles = "2")]
    public class ReceptionController : Controller
    {
        private readonly RecruitmentCrmContext _context;

        public ReceptionController(RecruitmentCrmContext context)
        {
            _context = context;
        }

        private Guid CurrentUserId =>
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private string CurrentUserName =>
            User.FindFirstValue(ClaimTypes.Name) ?? "استقبال";

        // ── GET /Reception/Index ──────────────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            var today = DateTime.UtcNow.Date;

            var todayVisits = await _context.LeadVisits
                .Include(v => v.Lead)
                .Where(v => v.VisitDateTime >= today)
                .OrderByDescending(v => v.VisitDateTime)
                .Select(v => new WalkInSearchResultDto
                {
                    LeadId = v.LeadId,
                    FullName = v.Lead.FullName,
                    Phone = v.Lead.Phone,
                    LeadCode = v.Lead.LeadCode ?? string.Empty,
                    Status = v.Lead.Status,
                    IsConverted = v.Lead.IsConverted,
                    AssignedSalesName = v.AssignedSalesUser != null ? v.AssignedSalesUser.FullName : null
                })
                .ToListAsync();

            ViewBag.TodayVisitsCount = todayVisits.Count;
            ViewBag.TodayVisits = todayVisits;

            return View();
        }

        // ── GET /Reception/WalkIn ─────────────────────────────────────────────
        public IActionResult WalkIn()
        {
            return View();
        }

        // ── POST /Reception/Search ────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Search(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
            {
                TempData["Error"] = "يرجى إدخال رقم الهاتف";
                return RedirectToAction("WalkIn");
            }

            var lead = await _context.Leads
                .Include(l => l.AssignedSales)
                .FirstOrDefaultAsync(l => l.Phone == phone.Trim());

            if (lead == null)
            {
                TempData["NewWalkInPhone"] = phone.Trim();
                TempData["Info"] = "لم يتم العثور على العميل — يمكنك تسجيله كعميل جديد";
                return RedirectToAction("NewWalkIn");
            }

            var result = new WalkInSearchResultDto
            {
                LeadId = lead.Id,
                FullName = lead.FullName,
                Phone = lead.Phone,
                LeadCode = lead.LeadCode ?? string.Empty,
                Status = lead.Status,
                IsConverted = lead.IsConverted,
                ConvertedCandidateId = lead.ConvertedCandidateId,
                AssignedSalesName = lead.AssignedSales?.FullName
            };

            var officeSalesUsers = await _context.Users
                .Where(u => u.Role == 6 && u.IsActive == true)
                .Select(u => new UserListItemDto
                {
                    Id = u.Id,
                    FullName = u.FullName,
                    Email = u.Email,
                    Role = u.Role,
                    IsActive = u.IsActive
                })
                .ToListAsync();

            ViewBag.OfficeSalesUsers = officeSalesUsers;
            return View("CheckIn", result);
        }

        // ── POST /Reception/CheckIn ───────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckIn(WalkInCheckInDto dto)
        {
            var userId = CurrentUserId;

            var currentUser = await _context.Users.FindAsync(userId);
            if (currentUser == null) return Unauthorized();

            var lead = await _context.Leads
                .FirstOrDefaultAsync(l => l.Id == dto.LeadId);

            if (lead == null)
            {
                TempData["Error"] = "العميل غير موجود";
                return RedirectToAction("WalkIn");
            }

            // Already at office today — just update assignment if changed
            var alreadyVisitedToday = await _context.LeadVisits
                .AnyAsync(v => v.LeadId == dto.LeadId
                    && v.VisitDateTime >= DateTime.UtcNow.Date);

            if (alreadyVisitedToday)
            {
                // Just update office sales assignment if provided
                if (dto.OfficeSalesId.HasValue)
                {
                    lead.AssignedOfficeSalesId = dto.OfficeSalesId.Value;
                    lead.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    TempData["Success"] = $"تم تحديث تعيين {lead.FullName}";
                }
                else
                {
                    TempData["Info"] = $"العميل {lead.FullName} مسجل زيارة اليوم بالفعل";
                }
                return RedirectToAction("Index");
            }

            // New visit — log it
            _context.LeadVisits.Add(new LeadVisit
            {
                Id = Guid.NewGuid(),
                LeadId = dto.LeadId,
                ReceptionUserId = userId,
                BranchId = currentUser.BranchId,
                AssignedSalesUserId = dto.OfficeSalesId,
                JobPackageId = dto.JobPackageId,
                Notes = dto.Notes,
                MeetingOutcome = 1,
                VisitDateTime = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });

            var oldStatus = lead.Status;
            lead.Status = 6;
            lead.UpdatedAt = DateTime.UtcNow;

            if (dto.OfficeSalesId.HasValue)
                lead.AssignedOfficeSalesId = dto.OfficeSalesId.Value;

            _context.LeadActivities.Add(new LeadActivity
            {
                Id = Guid.NewGuid(),
                LeadId = dto.LeadId,
                ActivityType = 2,
                Description = $"زيارة مكتب — تم الاستقبال بواسطة {CurrentUserName}" +
                              (dto.OfficeSalesId.HasValue ? " · تم التعيين لمندوب المبيعات" : "") +
                              (string.IsNullOrEmpty(dto.Notes) ? "" : $" · {dto.Notes}"),
                CreatedById = userId,
                CreatedByName = CurrentUserName,
                ActorType = 1,
                CreatedAt = DateTime.UtcNow
            });

            _context.LeadFunnelHistories.Add(new LeadFunnelHistory
            {
                Id = Guid.NewGuid(),
                LeadId = dto.LeadId,
                FromStatus = oldStatus,
                ToStatus = 6,
                ChangedById = userId,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم تسجيل زيارة {lead.FullName} بنجاح";
            return RedirectToAction("Index");
        }

        // ── GET /Reception/NewWalkIn ──────────────────────────────────────────
        public async Task<IActionResult> NewWalkIn()
        {
            var officeSalesUsers = await _context.Users
                .Where(u => u.Role == 6 && u.IsActive == true)
                .Select(u => new UserListItemDto
                {
                    Id = u.Id,
                    FullName = u.FullName,
                    Email = u.Email,
                    Role = u.Role,
                    IsActive = u.IsActive
                })
                .ToListAsync();

            var campaigns = await _context.Campaigns
                .Where(c => c.Status == 1)
                .Select(c => new CampaignListItemDto { Id = c.Id, Name = c.Name })
                .ToListAsync();

            ViewBag.OfficeSalesUsers = officeSalesUsers;
            ViewBag.Campaigns = campaigns;

            return View();
        }

        // ── POST /Reception/CreateWalkIn ──────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateWalkIn(WalkInCheckInDto dto)
        {
            var userId = CurrentUserId;

            var existing = await _context.Leads
                .FirstOrDefaultAsync(l => l.Phone == dto.Phone);

            if (existing != null)
            {
                TempData["Error"] = $"يوجد عميل مسجل بهذا الرقم بالفعل — {existing.FullName} ({existing.LeadCode})";
                return RedirectToAction("WalkIn");
            }

            var currentUser = await _context.Users.FindAsync(userId);
            if (currentUser == null) return Unauthorized();

            var leadId = Guid.NewGuid();

            _context.Leads.Add(new Lead
            {
                Id = leadId,
                BranchId = currentUser.BranchId,
                RegisteredById = userId,
                AssignedOfficeSalesId = dto.OfficeSalesId,
                CampaignId = dto.CampaignId,
                FullName = dto.FullName!,
                Phone = dto.Phone!,
                LeadSource = 3,
                Status = 6,
                InterestedJobTitle = dto.InterestedJobTitle,
                InterestedCountry = dto.InterestedCountry,
                Notes = dto.Notes,
                CreatedAt = DateTime.UtcNow
            });

            _context.LeadVisits.Add(new LeadVisit
            {
                Id = Guid.NewGuid(),
                LeadId = leadId,
                ReceptionUserId = userId,
                BranchId = currentUser.BranchId,
                AssignedSalesUserId = dto.OfficeSalesId,
                JobPackageId = dto.JobPackageId,
                Notes = dto.Notes,
                MeetingOutcome = 1,
                VisitDateTime = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });

            _context.LeadActivities.Add(new LeadActivity
            {
                Id = Guid.NewGuid(),
                LeadId = leadId,
                ActivityType = 2,
                Description = $"تسجيل زيارة مكتب جديدة — بواسطة {CurrentUserName}",
                CreatedById = userId,
                CreatedByName = CurrentUserName,
                ActorType = 1,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم تسجيل {dto.FullName} بنجاح";
            return RedirectToAction("Index");
        }

        // ── GET /Reception/Leads ──────────────────────────────────────────────
        public async Task<IActionResult> Leads(int page = 1)
        {
            const int pageSize = 20;

            var query = _context.Leads
                .Where(l => l.LeadSource == 3 || l.Status == 6);

            var totalCount = await query.CountAsync();

            var leads = await query
                .OrderByDescending(l => l.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(l => new LeadListItemDto
                {
                    Id = l.Id,
                    LeadCode = l.LeadCode ?? string.Empty,
                    FullName = l.FullName,
                    Phone = l.Phone,
                    Status = l.Status,
                    IsConverted = l.IsConverted,
                    CreatedAt = l.CreatedAt
                })
                .ToListAsync();

            ViewBag.CurrentPage = page;
            ViewBag.TotalCount = totalCount;

            return View(leads);
        }
    }
}
