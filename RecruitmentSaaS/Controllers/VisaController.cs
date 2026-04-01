using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.Data;
using System.Security.Claims;

namespace RecruitmentSaaS.Controllers
{
    [Authorize(Roles = "1,5")]
    public class VisaController : Controller
    {
        private readonly RecruitmentCrmContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly IVisaParserService _parser;
        private readonly INotificationService _notifications;

        public VisaController(
            RecruitmentCrmContext context,
            IWebHostEnvironment env,
            IVisaParserService parser,
            INotificationService notifications)
        {
            _context = context;
            _env = env;
            _parser = parser;
            _notifications = notifications;
        }

        private Guid CurrentUserId =>
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // ── GET /Visa/Index ───────────────────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            var pending = await _context.VisaUploads
     .Include(v => v.UploadedBy)
     .Include(v => v.MatchedCandidate)
     .Where(v => v.MatchStatus == 0)
     .OrderByDescending(v => v.UploadedAt)
     .ToListAsync();
            var matched = await _context.VisaUploads
                .Include(v => v.UploadedBy)
                .Include(v => v.MatchedCandidate)
                .Where(v => v.MatchStatus == 1 || v.MatchStatus == 2)
                .OrderByDescending(v => v.UploadedAt)
                .Take(20)
                .ToListAsync();

            ViewBag.PendingUploads = pending;
            ViewBag.MatchedUploads = matched;
            ViewBag.PendingCount = pending.Count;

            return View();
        }

        // ── POST /Visa/Upload ─────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(List<IFormFile> files)
        {
            if (files == null || !files.Any())
            {
                TempData["Error"] = "يرجى اختيار ملف واحد على الأقل";
                return RedirectToAction("Index");
            }

            int matched = 0;
            int unmatched = 0;

            foreach (var file in files)
            {
                if (file.Length == 0) continue;
                if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Save file to disk
                var uploadDir = Path.Combine(_env.WebRootPath, "uploads", "visas");
                Directory.CreateDirectory(uploadDir);
                var uniqueName = $"{Guid.NewGuid()}.pdf";
                var filePath = Path.Combine(uploadDir, uniqueName);
                var fileKey = $"uploads/visas/{uniqueName}";

                using (var stream = new FileStream(filePath, FileMode.Create))
                    await file.CopyToAsync(stream);

                // Parse PDF
                VisaExtractedData extracted;
                using (var readStream = System.IO.File.OpenRead(filePath))
                    extracted = _parser.ParsePdf(readStream);

                // Try to match with candidate by passport number
                Candidate? matchedCandidate = null;
                byte matchStatus = 0;

                if (!string.IsNullOrEmpty(extracted.PassportNumber))
                {
                    matchedCandidate = await _context.Candidates
                        .Include(c => c.JobPackage)
                            .ThenInclude(p => p.PackageStages.OrderBy(s => s.StageOrder))
                                .ThenInclude(s => s.StageType)
                        .FirstOrDefaultAsync(c =>
                            c.PassportNumber != null &&
                            c.PassportNumber.ToUpper() == extracted.PassportNumber.ToUpper());

                    if (matchedCandidate != null)
                    {
                        matchStatus = 1; // Auto matched
                        matched++;

                        // Apply visa data and move stage
                        await ApplyVisaToCandidate(matchedCandidate, extracted, fileKey, file.FileName);
                    }
                    else
                    {
                        matchStatus = 0; // No match — pending manual
                        unmatched++;
                    }
                }
                else
                {
                    matchStatus = 0;
                    unmatched++;
                }

                // Save upload record
                _context.VisaUploads.Add(new VisaUpload
                {
                    Id = Guid.NewGuid(),
                    UploadedById = CurrentUserId,
                    UploadedAt = DateTime.UtcNow,
                    FileName = file.FileName,
                    FileKey = fileKey,
                    ExtractedPassportNo = extracted.PassportNumber,
                    ExtractedVisaNo = extracted.VisaNumber,
                    ExtractedVisaExpiry = extracted.VisaExpiry,
                    ExtractedFullName = extracted.FullName,
                    MatchStatus = matchStatus,
                    MatchedCandidateId = matchedCandidate?.Id
                });
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم رفع {files.Count} ملف — تطابق: {matched} · بدون تطابق: {unmatched}";
            return RedirectToAction("Index");
        }

        // ── POST /Visa/ManualMatch ────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ManualMatch(Guid visaUploadId, Guid candidateId)
        {
            var upload = await _context.VisaUploads
                .FirstOrDefaultAsync(v => v.Id == visaUploadId && v.MatchStatus == 0);

            if (upload == null)
            {
                TempData["Error"] = "الملف غير موجود أو تمت معالجته";
                return RedirectToAction("Index");
            }

            var candidate = await _context.Candidates
                .Include(c => c.JobPackage)
                    .ThenInclude(p => p.PackageStages.OrderBy(s => s.StageOrder))
                        .ThenInclude(s => s.StageType)
                .FirstOrDefaultAsync(c => c.Id == candidateId);

            if (candidate == null)
            {
                TempData["Error"] = "المرشح غير موجود";
                return RedirectToAction("Index");
            }

            var extracted = new VisaExtractedData
            {
                PassportNumber = upload.ExtractedPassportNo,
                VisaNumber = upload.ExtractedVisaNo,
                VisaExpiry = upload.ExtractedVisaExpiry,
                FullName = upload.ExtractedFullName
            };

            await ApplyVisaToCandidate(candidate, extracted, upload.FileKey, upload.FileName);

            upload.MatchStatus = 2; // Manual match
            upload.MatchedCandidateId = candidateId;

            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم ربط التأشيرة بـ {candidate.FullName} بنجاح";
            return RedirectToAction("Index");
        }

        // ── POST /Visa/Dismiss ────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Dismiss(Guid visaUploadId, string? notes)
        {
            var upload = await _context.VisaUploads
                .FirstOrDefaultAsync(v => v.Id == visaUploadId);

            if (upload == null) return NotFound();

            upload.MatchStatus = 3; // No match / dismissed
            upload.Notes = notes;
            await _context.SaveChangesAsync();

            TempData["Success"] = "تم تجاهل هذه التأشيرة";
            return RedirectToAction("Index");
        }

        // ── GET /Visa/SearchCandidates (AJAX) ─────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> SearchCandidates(string q)
        {
            if (string.IsNullOrWhiteSpace(q))
                return Json(new List<object>());

            var results = await _context.Candidates
                .Where(c => c.FullName.Contains(q) || c.Phone.Contains(q)
                    || (c.PassportNumber != null && c.PassportNumber.Contains(q)))
                .OrderBy(c => c.FullName)
                .Take(10)
                .Select(c => new
                {
                    c.Id,
                    c.FullName,
                    c.PassportNumber,
                    c.Phone
                })
                .ToListAsync();

            return Json(results);
        }

        // ── Helper: Apply visa data to candidate ──────────────────────────────
        private async Task ApplyVisaToCandidate(
            Candidate candidate,
            VisaExtractedData extracted,
            string fileKey,
            string fileName)
        {
            // Save visa data
            if (!string.IsNullOrEmpty(extracted.VisaNumber))
                candidate.VisaNumber = extracted.VisaNumber;

            if (extracted.VisaExpiry.HasValue)
                candidate.VisaExpiry = extracted.VisaExpiry.Value;

            candidate.UpdatedAt = DateTime.UtcNow;

            // Save visa PDF as Document (DocumentType = 2 = تأشيرة)
            var fullFilePath = Path.Combine(_env.WebRootPath, fileKey.TrimStart('/'));
            var fileSize = System.IO.File.Exists(fullFilePath)
                ? (int)new System.IO.FileInfo(fullFilePath).Length
                : 1; // fallback — constraint requires > 0

            _context.Documents.Add(new Document
            {
                Id = Guid.NewGuid(),
                CandidateId = candidate.Id,
                UploadedById = CurrentUserId,
                DocumentType = 2,
                FileName = fileName,
                S3key = fileKey,
                FileSizeBytes = fileSize,
                MimeType = "application/pdf",
                UploadedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            // Move to VISA_ISSUED stage
            var visaStage = candidate.JobPackage?.PackageStages
                .FirstOrDefault(s => s.StageType?.StageCode == "VISA_ISSUED"
                                  || s.StageType?.StageCode == "VISA_RECEIVED");

            if (visaStage != null)
            {
                var spSuccess = new SqlParameter { ParameterName = "@Success", SqlDbType = SqlDbType.Bit, Direction = ParameterDirection.Output };
                var spMessage = new SqlParameter { ParameterName = "@Message", SqlDbType = SqlDbType.NVarChar, Size = 500, Direction = ParameterDirection.Output };
                var spStage = new SqlParameter { ParameterName = "@NewStageName", SqlDbType = SqlDbType.NVarChar, Size = 200, Direction = ParameterDirection.Output };

                await _context.Database.ExecuteSqlRawAsync(
                    "EXEC demorecruitment.sp_MoveToNextStage @CandidateId, @MovedById, @Notes, @IsOverride, @OverrideReason, @Success OUTPUT, @Message OUTPUT, @NewStageName OUTPUT",
                    new SqlParameter("@CandidateId", candidate.Id),
                    new SqlParameter("@MovedById", CurrentUserId),
                    new SqlParameter("@Notes", (object)"تم استلام التأشيرة"),
                    new SqlParameter("@IsOverride", true),
                    new SqlParameter("@OverrideReason", "visa_upload"),
                    spSuccess, spMessage, spStage
                );

                // Notify Sales
                if (candidate.AssignedSalesId != Guid.Empty)
                {
                    await _notifications.SendAsync(
                        userId: candidate.AssignedSalesId,
                        title: "تم استلام التأشيرة ✅",
                        message: $"تم رفع تأشيرة {candidate.FullName} — {extracted.VisaNumber}",
                        link: $"/Sales/CandidateDetail/{candidate.Id}",
                        type: NotificationType.StageMove
                    );
                }
            }
        }
    }
}
