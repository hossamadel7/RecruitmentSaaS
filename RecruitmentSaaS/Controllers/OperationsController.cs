using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using System.Security.Claims;

namespace RecruitmentSaaS.Controllers
{
    [Authorize(Roles = "5")]
    public class OperationsController : Controller
    {
        private readonly RecruitmentCrmContext _context;
        private readonly IWebHostEnvironment _env;

        public OperationsController(RecruitmentCrmContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
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
                .Where(c => c.IsCompleted != true && c.CurrentPackageStageId != null)
                .GroupBy(c => new
                {
                    Name = c.CurrentPackageStage!.StageName,
                    Order = c.CurrentPackageStage!.StageOrder
                })
                .Select(g => new
                {
                    Stage = g.Key.Name,
                    Order = g.Key.Order,
                    Count = g.Count()
                })
                .OrderBy(g => g.Order)
                .ToListAsync();

            ViewBag.TotalCandidates = totalCandidates;
            ViewBag.InProgress = inProgress;
            ViewBag.CompletedCount = completedCount;
            ViewBag.ByStage = byStage;

            return View();
        }

        // ── GET /Operations/Candidates ────────────────────────────────────────
        public async Task<IActionResult> Candidates(string? q, Guid? stageId, int page = 1)
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

            if (stageId.HasValue)
                query = query.Where(c => c.CurrentPackageStageId == stageId.Value);

            query = query.OrderByDescending(c => c.CreatedAt);

            var total = await query.CountAsync();

            var candidates = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.Q = q;
            ViewBag.StageId = stageId;
            ViewBag.Page = page;
            ViewBag.Total = total;
            ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);

            return View(candidates);
        }

        // ── GET /Operations/CandidateDetail/{id} ─────────────────────────────
        public async Task<IActionResult> CandidateDetail(Guid id)
        {
            var candidate = await _context.Candidates
                .Include(c => c.JobPackage)
                    .ThenInclude(p => p.PackageStages.Where(s => s.IsActive == true).OrderBy(s => s.StageOrder))
                    .ThenInclude(ps => ps.StageType)
                .Include(c => c.RegisteredBy)
                .Include(c => c.CurrentPackageStage)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (candidate == null) return NotFound();

            var payments = await _context.Payments
                .Include(p => p.RecordedBy)
                .Include(p => p.ApprovedBy)
                .Where(p => p.CandidateId == id)
                .OrderByDescending(p => p.PaymentDate)
                .Select(p => new RecruitmentSaaS.Models.DTOs.PaymentDto
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
                .Select(d => new RecruitmentSaaS.Models.DTOs.DocumentDto
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
                .Select(h => new RecruitmentSaaS.Models.DTOs.CandidateStageHistoryDto
                {
                    FromStage = h.FromStage,
                    ToStage = h.ToStage,
                    ToStageOrder = (int)h.ToStage,
                    IsOverride = h.IsOverride,
                    OverrideReason = h.OverrideReason,
                    Notes = h.Notes,
                    ChangedByName = h.ChangedBy.FullName,
                    CreatedAt = h.CreatedAt
                })
                .ToListAsync();

            var visits = await _context.LeadVisits
                .Include(v => v.ReceptionUser)
                .Include(v => v.AssignedSalesUser)
                .Where(v => v.Lead.ConvertedCandidateId == id)
                .OrderByDescending(v => v.VisitDateTime)
                .Select(v => new RecruitmentSaaS.Models.DTOs.VisitDto
                {
                    Id = v.Id,
                    VisitDateTime = v.VisitDateTime,
                    ReceptionUserName = v.ReceptionUser.FullName,
                    Notes = v.Notes,
                    AssignedSalesName = v.AssignedSalesUser != null ? v.AssignedSalesUser.FullName : null
                })
                .ToListAsync();

            var visitComments = await _context.CandidateStageHistories
                .Include(h => h.ChangedBy)
                .Where(h => h.CandidateId == id
                    && h.OverrideReason != null
                    && h.OverrideReason.StartsWith("visit:"))
                .OrderBy(h => h.CreatedAt)
                .Select(h => new RecruitmentSaaS.Models.DTOs.CandidateStageHistoryDto
                {
                    FromStage = h.FromStage,
                    ToStage = h.ToStage,
                    ToStageOrder = (int)h.ToStage,
                    IsOverride = h.IsOverride,
                    OverrideReason = h.OverrideReason,
                    Notes = h.Notes,
                    ChangedByName = h.ChangedBy.FullName,
                    CreatedAt = h.CreatedAt
                })
                .ToListAsync();

            ViewBag.Payments = payments;
            ViewBag.Documents = documents;
            ViewBag.StageHistory = history;
            ViewBag.Visits = visits;
            ViewBag.VisitComments = visitComments;
            ViewBag.PackageStages = candidate.JobPackage.PackageStages
                                        .Where(s => s.IsActive == true)
                                        .OrderBy(s => s.StageOrder)
                                        .ToList();

            var dto = new RecruitmentSaaS.Models.DTOs.CandidateDetailDto
            {
                Id = candidate.Id,
                FullName = candidate.FullName,
                Phone = candidate.Phone,
                NationalId = candidate.NationalId,
                PassportNumber = candidate.PassportNumber,
                PassportExpiry = candidate.PassportExpiry,
                Age = candidate.Age,
                City = candidate.City,
                Notes = candidate.Notes,
                CurrentStageName = candidate.CurrentPackageStage?.StageName ?? "—",
                CurrentStageOrder = candidate.CurrentPackageStage?.StageOrder ?? 0,
                Status = candidate.Status,
                JobPackageName = candidate.JobPackage.Name,
                TotalPaidEGP = candidate.TotalPaidEgp,
                IsProfileComplete = candidate.IsProfileComplete,
                IsCompleted = candidate.IsCompleted,
                CreatedAt = candidate.CreatedAt,
                FlightDate = candidate.FlightDate
            };

            // Required by shared Sales_CandidateDetail view
            ViewBag.StageCompletions = await _context.StageActionCompletions
                .Where(sc => sc.CandidateId == id)
                .ToListAsync();

            ViewBag.PendingApproval = await _context.StageApprovalRequests
                .Include(r => r.ToStage)
                .FirstOrDefaultAsync(r => r.CandidateId == id && r.Status == 1);

            return View("~/Views/Sales/CandidateDetail.cshtml", dto);
        }

        // ── POST /Operations/MoveToNextStage ─────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MoveToNextStage(Guid candidateId, string? notes)
        {
            var candidate = await _context.Candidates
                .FirstOrDefaultAsync(c => c.Id == candidateId);

            if (candidate == null)
            {
                TempData["Error"] = "المرشح غير موجود";
                return RedirectToAction("Candidates");
            }

            var successParam = new Microsoft.Data.SqlClient.SqlParameter
            {
                ParameterName = "@Success",
                SqlDbType = System.Data.SqlDbType.Bit,
                Direction = System.Data.ParameterDirection.Output
            };
            var messageParam = new Microsoft.Data.SqlClient.SqlParameter
            {
                ParameterName = "@Message",
                SqlDbType = System.Data.SqlDbType.NVarChar,
                Size = 500,
                Direction = System.Data.ParameterDirection.Output
            };
            var stageNameParam = new Microsoft.Data.SqlClient.SqlParameter
            {
                ParameterName = "@NewStageName",
                SqlDbType = System.Data.SqlDbType.NVarChar,
                Size = 200,
                Direction = System.Data.ParameterDirection.Output
            };

            await _context.Database.ExecuteSqlRawAsync(
                "EXEC demorecruitment.sp_MoveToNextStage @CandidateId, @MovedById, @Notes, @IsOverride, @OverrideReason, @Success OUTPUT, @Message OUTPUT, @NewStageName OUTPUT",
                new Microsoft.Data.SqlClient.SqlParameter("@CandidateId", candidateId),
                new Microsoft.Data.SqlClient.SqlParameter("@MovedById", CurrentUserId),
                new Microsoft.Data.SqlClient.SqlParameter("@Notes", (object?)notes ?? DBNull.Value),
                new Microsoft.Data.SqlClient.SqlParameter("@IsOverride", false),
                new Microsoft.Data.SqlClient.SqlParameter("@OverrideReason", DBNull.Value),
                successParam, messageParam, stageNameParam
            );

            var success = (bool)successParam.Value;
            var message = messageParam.Value?.ToString() ?? "";

            if (success)
                TempData["Success"] = message;
            else
                TempData["Error"] = message;

            return RedirectToAction("CandidateDetail", new { id = candidateId });
        }

        // ── POST /Operations/AddVisitComment ──────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddVisitComment(Guid candidateId, Guid visitId, string comment)
        {
            var candidate = await _context.Candidates
                .FirstOrDefaultAsync(c => c.Id == candidateId);

            if (candidate == null)
            {
                TempData["Error"] = "المرشح غير موجود";
                return RedirectToAction("Candidates");
            }

            _context.CandidateStageHistories.Add(new RecruitmentSaaS.Models.Entities.CandidateStageHistory
            {
                Id = Guid.NewGuid(),
                CandidateId = candidateId,
                FromStageId = candidate.CurrentPackageStageId,
                ToStageId = candidate.CurrentPackageStageId,
                ChangedById = CurrentUserId,
                IsOverride = false,
                OverrideReason = $"visit:{visitId}:{comment}",
                MeetingOutcome = comment,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            TempData["Success"] = "تم إضافة التعليق بنجاح";
            return RedirectToAction("CandidateDetail", new { id = candidateId });
        }

        // ── POST /Operations/UploadDocument ───────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadDocument(Guid candidateId, byte documentType, IFormFile file)
        {
            var candidate = await _context.Candidates
                .FirstOrDefaultAsync(c => c.Id == candidateId);

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

            const long maxSize = 10 * 1024 * 1024;
            if (file.Length > maxSize)
            {
                TempData["Error"] = "حجم الملف يتجاوز الحد المسموح (10 MB)";
                return RedirectToAction("CandidateDetail", new { id = candidateId });
            }

            var uploadFolder = Path.Combine(_env.WebRootPath, "uploads", "candidates", candidateId.ToString());
            Directory.CreateDirectory(uploadFolder);

            var ext = Path.GetExtension(file.FileName);
            var uniqueFileName = $"{Guid.NewGuid()}{ext}";
            var filePath = Path.Combine(uploadFolder, uniqueFileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
                await file.CopyToAsync(stream);

            _context.Documents.Add(new RecruitmentSaaS.Models.Entities.Document
            {
                Id = Guid.NewGuid(),
                CandidateId = candidateId,
                UploadedById = CurrentUserId,
                DocumentType = documentType,
                FileName = file.FileName,
                S3key = $"candidates/{candidateId}/{uniqueFileName}",
                FileSizeBytes = (int)file.Length,
                MimeType = file.ContentType,
                UploadedAt = DateTime.UtcNow
            });

            candidate.IsProfileComplete = true;
            candidate.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            TempData["Success"] = "تم رفع المستند بنجاح";
            return RedirectToAction("CandidateDetail", new { id = candidateId });
        }

        // ── POST /Operations/SaveFlightDate ──────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveFlightDate(Guid candidateId, DateTime flightDate)
        {
            var userId = CurrentUserId;

            var candidate = await _context.Candidates
                .Include(c => c.CurrentPackageStage)
                .FirstOrDefaultAsync(c => c.Id == candidateId);

            if (candidate == null)
            {
                TempData["Error"] = "المرشح غير موجود";
                return RedirectToAction("Candidates");
            }

            candidate.FlightDate = flightDate;
            candidate.UpdatedAt = DateTime.UtcNow;

            if (candidate.CurrentPackageStageId.HasValue)
            {
                var alreadyDone = await _context.StageActionCompletions
                    .AnyAsync(s => s.CandidateId == candidateId
                                && s.PackageStageId == candidate.CurrentPackageStageId.Value
                                && s.CompletionType == 6);

                if (!alreadyDone)
                {
                    _context.StageActionCompletions.Add(new StageActionCompletion
                    {
                        Id = Guid.NewGuid(),
                        CandidateId = candidateId,
                        PackageStageId = candidate.CurrentPackageStageId.Value,
                        CompletedAt = DateTime.UtcNow,
                        CompletedById = userId,
                        CompletionType = 6,
                        Notes = $"تاريخ الطيران: {flightDate:dd/MM/yyyy HH:mm}"
                    });
                }
                else
                {
                    var existing = await _context.StageActionCompletions
                        .FirstOrDefaultAsync(s => s.CandidateId == candidateId
                                                && s.PackageStageId == candidate.CurrentPackageStageId.Value
                                                && s.CompletionType == 6);
                    if (existing != null)
                        existing.Notes = $"تاريخ الطيران: {flightDate:dd/MM/yyyy HH:mm}";
                }
            }

            var reminderDate = DateOnly.FromDateTime(flightDate.AddDays(-1));
            var linkedLeadId = await _context.Leads
                .Where(l => l.ConvertedCandidateId == candidateId)
                .Select(l => (Guid?)l.Id)
                .FirstOrDefaultAsync();

            if (linkedLeadId.HasValue)
            {
                var oldReminders = await _context.FollowUpReminders
                    .Where(r => r.LeadId == linkedLeadId.Value
                             && r.Notes != null
                             && r.Notes.Contains("طيران")
                             && r.Status == 1)
                    .ToListAsync();
                _context.FollowUpReminders.RemoveRange(oldReminders);

                if (candidate.AssignedSalesId != Guid.Empty)
                {
                    _context.FollowUpReminders.Add(new FollowUpReminder
                    {
                        Id = Guid.NewGuid(),
                        LeadId = linkedLeadId.Value,
                        AssignedToId = candidate.AssignedSalesId,
                        CreatedById = userId,
                        ReminderDate = reminderDate,
                        Notes = $"تذكير: رحلة {candidate.FullName} غداً الساعة {flightDate:HH:mm}",
                        Status = 1,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم حفظ تاريخ الطيران ✅ — {flightDate:dd/MM/yyyy HH:mm}";
            return RedirectToAction("CandidateDetail", new { id = candidateId });
        }

        // ── POST /Operations/SetReminder ──────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetReminder(Guid candidateId, DateOnly reminderDate, string? notes)
        {
            var userId = CurrentUserId;

            var candidate = await _context.Candidates
                .FirstOrDefaultAsync(c => c.Id == candidateId);

            if (candidate == null)
            {
                TempData["Error"] = "المرشح غير موجود";
                return RedirectToAction("Candidates");
            }

            var leadId = await _context.Leads
                .Where(l => l.ConvertedCandidateId == candidateId)
                .Select(l => (Guid?)l.Id)
                .FirstOrDefaultAsync();

            if (leadId.HasValue && candidate.AssignedSalesId != Guid.Empty)
            {
                _context.FollowUpReminders.Add(new FollowUpReminder
                {
                    Id = Guid.NewGuid(),
                    LeadId = leadId.Value,
                    AssignedToId = candidate.AssignedSalesId,
                    CreatedById = userId,
                    ReminderDate = reminderDate,
                    Notes = notes,
                    Status = 1,
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();
            }

            TempData["Success"] = "تم تعيين التذكير بنجاح";
            return RedirectToAction("CandidateDetail", new { id = candidateId });
        }

        // ── GET /Operations/DownloadDocument/{id} ─────────────────────────────
        public async Task<IActionResult> DownloadDocument(Guid id)
        {
            var doc = await _context.Documents
                .Include(d => d.Candidate)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (doc == null) return NotFound();

            var filePath = Path.Combine(_env.WebRootPath, "uploads", "candidates",
                doc.CandidateId.ToString(),
                Path.GetFileName(doc.S3key));

            if (!System.IO.File.Exists(filePath)) return NotFound();

            var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
            return File(bytes, doc.MimeType, doc.FileName);
        }

        // ── POST /Operations/RequestRefund ────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestRefund(Guid candidateId, decimal amountEgp, string reason)
        {
            var candidate = await _context.Candidates
                .FirstOrDefaultAsync(c => c.Id == candidateId);

            if (candidate == null)
            {
                TempData["Error"] = "المرشح غير موجود";
                return RedirectToAction("Candidates");
            }

            _context.Refunds.Add(new RecruitmentSaaS.Models.Entities.Refund
            {
                Id = Guid.NewGuid(),
                CandidateId = candidateId,
                AmountEgp = amountEgp,
                Reason = reason,
                Status = 1,
                RequestedById = CurrentUserId,
                RequestedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            TempData["Success"] = "تم إرسال طلب الاسترداد للإدارة بنجاح";
            return RedirectToAction("CandidateDetail", new { id = candidateId });
        }
    }
}