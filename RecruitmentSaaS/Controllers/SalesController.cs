using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.DTOs;
using RecruitmentSaaS.Models.Entities;
using System.Data;
using System.Security.Claims;

namespace RecruitmentSaaS.Controllers
{
    [Authorize(Roles = "6")]
    public class SalesController : Controller
    {
        private readonly RecruitmentCrmContext _context;
        private readonly IWebHostEnvironment _env;

        public SalesController(RecruitmentCrmContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        private Guid CurrentUserId =>
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private string CurrentUserName =>
            User.FindFirstValue(ClaimTypes.Name) ?? "مستخدم";

        // ── GET /Sales/Index ──────────────────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            var userId = CurrentUserId;
            var now = DateTime.UtcNow;

            var waitingLeads = await _context.Leads
                .Where(l => l.AssignedOfficeSalesId == userId
                    && l.Status == 6)
                .OrderBy(l => l.UpdatedAt)
                .Select(l => new LeadListItemDto
                {
                    Id = l.Id,
                    LeadCode = l.LeadCode ?? string.Empty,
                    FullName = l.FullName,
                    Phone = l.Phone,
                    Status = l.Status,
                    IsConverted = l.IsConverted,
                    CreatedAt = l.CreatedAt,
                    LastContactedAt = l.LastContactedAt
                })
                .ToListAsync();

            var dto = new SalesDashboardDto
            {
                MyLeadsTotal = await _context.Leads
                    .CountAsync(l => l.AssignedOfficeSalesId == userId),

                MyLeadsNew = waitingLeads.Count,

                MyCandidatesTotal = await _context.Candidates
                    .CountAsync(c => c.AssignedSalesId == userId),

                DealsThisMonth = await _context.Candidates
                    .CountAsync(c => c.AssignedSalesId == userId
                        && c.IsCompleted == true
                        && c.CompletedAt != null
                        && c.CompletedAt.Value.Month == now.Month
                        && c.CompletedAt.Value.Year == now.Year),

                TodayReminders = new List<FollowUpReminderDto>(),
                RecentLeads = waitingLeads
            };

            return View(dto);
        }

        // ── GET /Sales/LeadDetail/{id} ────────────────────────────────────────
        public async Task<IActionResult> LeadDetail(Guid id)
        {
            var userId = CurrentUserId;

            var lead = await _context.Leads
                .Include(l => l.AssignedSales)
                .Include(l => l.AssignedOfficeSales)
                .Include(l => l.Campaign)
                .Include(l => l.RegisteredBy)
                .FirstOrDefaultAsync(l => l.Id == id
                    && l.AssignedOfficeSalesId == userId);

            if (lead == null) return NotFound();

            var activities = await _context.LeadActivities
                .Where(a => a.LeadId == id)
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            var jobPackages = await _context.JobPackages
                .Where(j => j.IsActive)
                .OrderBy(j => j.Name)
                .Select(j => new JobPackageListItemDto
                {
                    Id = j.Id,
                    Name = j.Name,
                    DestinationCountry = j.DestinationCountry,
                    JobTitle = j.JobTitle,
                    PriceEgp = j.PriceEgp
                })
                .ToListAsync();

            ViewBag.JobPackages = jobPackages;

            var dto = new LeadDetailDto
            {
                Id = lead.Id,
                LeadCode = lead.LeadCode ?? string.Empty,
                FullName = lead.FullName,
                Phone = lead.Phone,
                LeadSource = lead.LeadSource,
                Status = lead.Status,
                Notes = lead.Notes,
                InterestedJobTitle = lead.InterestedJobTitle,
                InterestedCountry = lead.InterestedCountry,
                ReferredByName = lead.ReferredByName,
                ReferredByPhone = lead.ReferredByPhone,
                IsConverted = lead.IsConverted,
                ConvertedAt = lead.ConvertedAt,
                ConvertedCandidateId = lead.ConvertedCandidateId,
                AssignedSalesName = lead.AssignedSales?.FullName,
                CampaignName = lead.Campaign?.Name,
                RegisteredByName = lead.RegisteredBy?.FullName,
                CreatedAt = lead.CreatedAt,
                LastContactedAt = lead.LastContactedAt,
                Activities = activities.Select(a => new LeadActivityDto
                {
                    Id = a.Id,
                    ActivityType = a.ActivityType,
                    Description = a.Description,
                    CreatedByName = a.CreatedByName,
                    ActorType = a.ActorType,
                    CreatedAt = a.CreatedAt
                }).ToList(),
                Reminders = new List<FollowUpReminderDto>()
            };

            return View(dto);
        }

        // ── POST /Sales/ConvertToCandidate ────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConvertToCandidate(Guid leadId, Guid jobPackageId)
        {
            var userId = CurrentUserId;

            var lead = await _context.Leads
                .FirstOrDefaultAsync(l => l.Id == leadId
                    && l.AssignedOfficeSalesId == userId
                    && l.IsConverted == false
                    && l.IsDuplicate == false);

            if (lead == null)
            {
                TempData["Error"] = "لا يمكن تحويل هذا العميل";
                return RedirectToAction("LeadDetail", new { id = leadId });
            }

            try
            {
                var candidateIdParam = new SqlParameter
                {
                    ParameterName = "@CandidateId",
                    SqlDbType = SqlDbType.UniqueIdentifier,
                    Direction = ParameterDirection.Output
                };

                await _context.Database.ExecuteSqlRawAsync(
                    "EXEC demorecruitment.sp_ConvertLeadToCandidate @LeadId, @JobPackageId, @ConvertedById, @CandidateId OUTPUT",
                    new SqlParameter("@LeadId", leadId),
                    new SqlParameter("@JobPackageId", jobPackageId),
                    new SqlParameter("@ConvertedById", userId),
                    candidateIdParam
                );

                var newCandidateId = (Guid)candidateIdParam.Value;

                var candidate = await _context.Candidates.FindAsync(newCandidateId);
                if (candidate != null)
                {
                    candidate.AssignedSalesId = userId;
                    await _context.SaveChangesAsync();
                }

                TempData["Success"] = "تم تحويل العميل إلى مرشح بنجاح";
                return RedirectToAction("CandidateDetail", new { id = newCandidateId });
            }
            catch (Exception ex)
            {
                TempData["Error"] = "حدث خطأ أثناء التحويل: " + ex.Message;
                return RedirectToAction("LeadDetail", new { id = leadId });
            }
        }

        // ── GET /Sales/Candidates ─────────────────────────────────────────────
        public async Task<IActionResult> Candidates(byte? stage)
        {
            var userId = CurrentUserId;

            var query = _context.Candidates
                .Include(c => c.JobPackage)
                .Where(c => c.AssignedSalesId == userId);

            if (stage.HasValue)
                query = query.Where(c => c.CurrentStage == stage.Value);

            var candidates = await query
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new CandidateListItemDto
                {
                    Id = c.Id,
                    FullName = c.FullName,
                    Phone = c.Phone,
                    NationalId = c.NationalId,
                    CurrentStage = c.CurrentStage,
                    Status = c.Status,
                    JobPackageName = c.JobPackage.Name,
                    TotalPaidEGP = c.TotalPaidEgp,
                    IsProfileComplete = c.IsProfileComplete,
                    IsCompleted = c.IsCompleted,
                    CreatedAt = c.CreatedAt
                })
                .ToListAsync();

            ViewBag.CurrentStage = stage;
            return View(candidates);
        }

        // ── GET /Sales/CandidateDetail/{id} ──────────────────────────────────
        public async Task<IActionResult> CandidateDetail(Guid id)
        {
            var userId = CurrentUserId;

            var candidate = await _context.Candidates
                .Include(c => c.JobPackage)
                .Include(c => c.RegisteredBy)
                .FirstOrDefaultAsync(c => c.Id == id && c.AssignedSalesId == userId);

            if (candidate == null) return NotFound();

            // Reset lead status back to 7 so client disappears from dashboard
            var lead = await _context.Leads
                .FirstOrDefaultAsync(l => l.ConvertedCandidateId == id && l.Status == 6);
            if (lead != null)
            {
                lead.Status = 7;
                lead.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            var payments = await _context.Payments
                .Include(p => p.RecordedBy)
                .Include(p => p.ApprovedBy)
                .Where(p => p.CandidateId == id)
                .OrderByDescending(p => p.PaymentDate)
                .Select(p => new PaymentDto
                {
                    Id = p.Id,
                    AmountEGP = p.AmountEgp,
                    PaymentDate = p.PaymentDate,
                    PaymentMethod = p.PaymentMethod,
                    TransactionType = p.TransactionType,
                    Status = p.Status,
                    Notes = p.Notes,
                    RecordedByName = p.RecordedBy.FullName,
                    ApprovedByName = p.ApprovedBy != null ? p.ApprovedBy.FullName : null,
                    ApprovedAt = p.ApprovedAt,
                    RejectionReason = p.RejectionReason,
                    CreatedAt = p.CreatedAt
                })
                .ToListAsync();

            var documents = await _context.Documents
                .Include(d => d.UploadedBy)
                .Where(d => d.CandidateId == id)
                .OrderByDescending(d => d.UploadedAt)
                .Select(d => new DocumentDto
                {
                    Id = d.Id,
                    DocumentType = d.DocumentType,
                    FileName = d.FileName,
                    FileSizeBytes = d.FileSizeBytes,
                    MimeType = d.MimeType,
                    UploadedByName = d.UploadedBy.FullName,
                    UploadedAt = d.UploadedAt
                })
                .ToListAsync();

            var history = await _context.CandidateStageHistories
                .Include(h => h.ChangedBy)
                .Where(h => h.CandidateId == id
                    && (h.OverrideReason == null || !h.OverrideReason.StartsWith("visit:")))
                .OrderByDescending(h => h.CreatedAt)
                .Select(h => new CandidateStageHistoryDto
                {
                    FromStage = h.FromStage,
                    ToStage = h.ToStage,
                    IsOverride = h.IsOverride,
                    OverrideReason = h.OverrideReason,
                    ChangedByName = h.ChangedBy.FullName,
                    CreatedAt = h.CreatedAt
                })
                .ToListAsync();

            var visits = await _context.LeadVisits
                .Include(v => v.ReceptionUser)
                .Include(v => v.AssignedSalesUser)
                .Where(v => v.Lead.ConvertedCandidateId == id)
                .OrderByDescending(v => v.VisitDateTime)
                .Select(v => new VisitDto
                {
                    Id = v.Id,
                    VisitDateTime = v.VisitDateTime,
                    ReceptionUserName = v.ReceptionUser.FullName,
                    Notes = v.Notes,
                    AssignedSalesName = v.AssignedSalesUser != null ? v.AssignedSalesUser.FullName : null
                })
                .ToListAsync();

            // Load visit comments from stage history
            var visitComments = await _context.CandidateStageHistories
                .Include(h => h.ChangedBy)
                .Where(h => h.CandidateId == id
                    && h.OverrideReason != null
                    && h.OverrideReason.StartsWith("visit:"))
                .OrderBy(h => h.CreatedAt)
                .Select(h => new CandidateStageHistoryDto
                {
                    FromStage = h.FromStage,
                    ToStage = h.ToStage,
                    IsOverride = h.IsOverride,
                    OverrideReason = h.OverrideReason,
                    ChangedByName = h.ChangedBy.FullName,
                    CreatedAt = h.CreatedAt
                })
                .ToListAsync();

            ViewBag.Payments = payments;
            ViewBag.Documents = documents;
            ViewBag.StageHistory = history;
            ViewBag.Visits = visits;
            ViewBag.VisitComments = visitComments;

            var dto = new CandidateDetailDto
            {
                Id = candidate.Id,
                FullName = candidate.FullName,
                Phone = candidate.Phone,
                NationalId = candidate.NationalId,
                Age = candidate.Age,
                City = candidate.City,
                Notes = candidate.Notes,
                CurrentStage = candidate.CurrentStage,
                Status = candidate.Status,
                JobPackageName = candidate.JobPackage.Name,
                TotalPaidEGP = candidate.TotalPaidEgp,
                IsProfileComplete = candidate.IsProfileComplete,
                IsCompleted = candidate.IsCompleted,
                CreatedAt = candidate.CreatedAt
            };

            return View(dto);
        }

        // ── POST /Sales/AddVisitComment ───────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddVisitComment(Guid candidateId, Guid visitId, string comment)
        {
            var userId = CurrentUserId;

            var candidate = await _context.Candidates
                .FirstOrDefaultAsync(c => c.Id == candidateId && c.AssignedSalesId == userId);

            if (candidate == null)
            {
                TempData["Error"] = "المرشح غير موجود";
                return RedirectToAction("Candidates");
            }

            var visit = await _context.LeadVisits
                .FirstOrDefaultAsync(v => v.Id == visitId);

            if (visit == null)
            {
                TempData["Error"] = "الزيارة غير موجودة";
                return RedirectToAction("CandidateDetail", new { id = candidateId });
            }

            // Log comment as stage history entry linked to visitId
            _context.CandidateStageHistories.Add(new CandidateStageHistory
            {
                Id = Guid.NewGuid(),
                CandidateId = candidateId,
                FromStage = candidate.CurrentStage,
                ToStage = candidate.CurrentStage,
                ChangedById = userId,
                IsOverride = false,
                OverrideReason = $"visit:{visitId}:{comment}",
                MeetingOutcome = comment,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            TempData["Success"] = "تم إضافة التعليق بنجاح";
            return RedirectToAction("CandidateDetail", new { id = candidateId });
        }

        // ── POST /Sales/ToggleStage ───────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleStage(Guid candidateId, byte stage, bool markDone, string? comment)
        {
            var userId = CurrentUserId;

            var candidate = await _context.Candidates
                .FirstOrDefaultAsync(c => c.Id == candidateId && c.AssignedSalesId == userId);

            if (candidate == null)
            {
                TempData["Error"] = "المرشح غير موجود";
                return RedirectToAction("Candidates");
            }

            // Log the toggle action
            _context.CandidateStageHistories.Add(new CandidateStageHistory
            {
                Id = Guid.NewGuid(),
                CandidateId = candidateId,
                FromStage = candidate.CurrentStage,
                ToStage = stage,
                ChangedById = userId,
                IsOverride = !markDone,   // IsOverride=true means "un-done"
                OverrideReason = comment,
                MeetingOutcome = comment,
                CreatedAt = DateTime.UtcNow
            });

            // Update CurrentStage to highest completed stage
            if (markDone && stage > candidate.CurrentStage)
                candidate.CurrentStage = stage;
            else if (!markDone && stage == candidate.CurrentStage && stage > 1)
                candidate.CurrentStage = (byte)(stage - 1);

            // Handle completion
            if (markDone && stage == 7)
            {
                candidate.IsCompleted = true;
                candidate.CompletedAt = DateTime.UtcNow;
            }
            else if (!markDone && stage == 7)
            {
                candidate.IsCompleted = false;
                candidate.CompletedAt = null;
            }

            candidate.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            TempData["Success"] = markDone
                ? $"تم تعيين المرحلة مكتملة"
                : $"تم إلغاء إكمال المرحلة";

            return RedirectToAction("CandidateDetail", new { id = candidateId });
        }

        // ── POST /Sales/EditCandidate ─────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditCandidate(Guid candidateId, string fullName, string phone,
            string? nationalId, int? age, string? city, string? notes)
        {
            var userId = CurrentUserId;

            var candidate = await _context.Candidates
                .FirstOrDefaultAsync(c => c.Id == candidateId && c.AssignedSalesId == userId);

            if (candidate == null)
            {
                TempData["Error"] = "المرشح غير موجود";
                return RedirectToAction("Candidates");
            }

            candidate.FullName = fullName;
            candidate.Phone = phone;
            candidate.NationalId = nationalId;
            candidate.Age = age;
            candidate.City = city;
            candidate.Notes = notes;
            candidate.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            TempData["Success"] = "تم تحديث بيانات المرشح بنجاح";
            return RedirectToAction("CandidateDetail", new { id = candidateId });
        }

        // ── POST /Sales/AddPayment ────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddPayment(Guid candidateId, decimal amountEgp,
            DateOnly paymentDate, byte paymentMethod, byte transactionType, string? notes)
        {
            var userId = CurrentUserId;

            var candidate = await _context.Candidates
                .FirstOrDefaultAsync(c => c.Id == candidateId && c.AssignedSalesId == userId);

            if (candidate == null)
            {
                TempData["Error"] = "المرشح غير موجود";
                return RedirectToAction("Candidates");
            }

            _context.Payments.Add(new Payment
            {
                Id = Guid.NewGuid(),
                CandidateId = candidateId,
                RecordedById = userId,
                AmountEgp = amountEgp,
                PaymentDate = paymentDate,
                PaymentMethod = paymentMethod,
                TransactionType = transactionType,
                Notes = notes,
                Status = 1,  // Pending — TotalPaidEgp updated only after accountant approval
                CreatedAt = DateTime.UtcNow
            });

            // Do NOT update TotalPaidEgp here — accountant approves first
            candidate.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            TempData["Success"] = "تم تسجيل الدفعة بنجاح";
            return RedirectToAction("CandidateDetail", new { id = candidateId });
        }

        // ── POST /Sales/UploadDocument ────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadDocument(Guid candidateId, byte documentType, IFormFile file)
        {
            var userId = CurrentUserId;

            var candidate = await _context.Candidates
                .FirstOrDefaultAsync(c => c.Id == candidateId && c.AssignedSalesId == userId);

            if (candidate == null)
            {
                TempData["Error"] = "المرشح غير موجود";
                return RedirectToAction("Candidates");
            }

            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "يرجى اختيار ملف";
                return RedirectToAction("CandidateDetail", new { id = candidateId });
            }

            const long maxSize = 10 * 1024 * 1024; // 10 MB
            if (file.Length > maxSize)
            {
                TempData["Error"] = "حجم الملف يتجاوز الحد المسموح (10 MB)";
                return RedirectToAction("CandidateDetail", new { id = candidateId });
            }

            // Save file to local disk under wwwroot/uploads/candidates/{candidateId}/
            var uploadFolder = Path.Combine(_env.WebRootPath, "uploads", "candidates", candidateId.ToString());
            Directory.CreateDirectory(uploadFolder);

            var ext = Path.GetExtension(file.FileName);
            var uniqueFileName = $"{Guid.NewGuid()}{ext}";
            var filePath = Path.Combine(uploadFolder, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
                await file.CopyToAsync(stream);

            // S3Key stores the relative path for now (swap with S3 key later)
            var s3Key = $"candidates/{candidateId}/{uniqueFileName}";

            _context.Documents.Add(new Document
            {
                Id = Guid.NewGuid(),
                CandidateId = candidateId,
                UploadedById = userId,
                DocumentType = documentType,
                FileName = file.FileName,
                S3key = s3Key,
                FileSizeBytes = (int)file.Length,
                MimeType = file.ContentType,
                UploadedAt = DateTime.UtcNow
            });

            // Mark profile as complete if docs uploaded
            candidate.IsProfileComplete = true;
            candidate.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            TempData["Success"] = "تم رفع المستند بنجاح";
            return RedirectToAction("CandidateDetail", new { id = candidateId });
        }

        // ── GET /Sales/DownloadDocument/{id} ─────────────────────────────────
        public async Task<IActionResult> DownloadDocument(Guid id)
        {
            var userId = CurrentUserId;

            var doc = await _context.Documents
                .Include(d => d.Candidate)
                .FirstOrDefaultAsync(d => d.Id == id && d.Candidate.AssignedSalesId == userId);

            if (doc == null) return NotFound();

            var filePath = Path.Combine(_env.WebRootPath, "uploads", "candidates",
                doc.CandidateId.ToString(),
                Path.GetFileName(doc.S3key));

            if (!System.IO.File.Exists(filePath))
                return NotFound();

            var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
            return File(bytes, doc.MimeType, doc.FileName);
        }
    }
}
