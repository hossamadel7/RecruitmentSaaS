using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.Security.Claims;

namespace RecruitmentSaaS.Controllers
{
    [Authorize(Roles = "1")]
    public class ContractController : Controller
    {
        private readonly RecruitmentCrmContext _context;
        private readonly IContractParserService _parser;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<ContractController> _logger;

        public ContractController(
            RecruitmentCrmContext context,
            IContractParserService parser,
            IWebHostEnvironment env,
            ILogger<ContractController> logger)
        {
            _context = context;
            _parser = parser;
            _env = env;
            _logger = logger;
        }

        private Guid CurrentUserId =>
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // ── GET /Contract/Index ───────────────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            var pending = await _context.ContractUploads
                .Where(c => c.MatchStatus == 0)
                .Include(c => c.UploadedBy)
                .OrderByDescending(c => c.CreatedAt)
                .ToListAsync();

            var matched = await _context.ContractUploads
                .Where(c => c.MatchStatus == 1 || c.MatchStatus == 2)
                .Include(c => c.UploadedBy)
                .Include(c => c.MatchedCandidate)
                .OrderByDescending(c => c.CreatedAt)
                .Take(50)
                .ToListAsync();

            ViewBag.Pending = pending;
            ViewBag.Matched = matched;
            return View();
        }

        // ── POST /Contract/Upload ─────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(List<IFormFile> files)
        {
            if (files == null || !files.Any())
            {
                TempData["Error"] = "اختر ملف PDF واحد على الأقل";
                return RedirectToAction("Index");
            }

            int autoMatched = 0, pending = 0;
            var extractionLog = new List<string>();

            // Get CONTRACT_ISSUED stage for sp_MoveToNextStage
            var contractStage = await _context.PackageStages
                .Include(ps => ps.StageType)
                .Where(ps => ps.StageType != null && ps.StageType.StageCode == "CONTRACT_ISSUED" && ps.IsActive == true)
                .ToListAsync();

            var contractStageIds = contractStage.Select(s => s.Id).ToHashSet();

            foreach (var file in files)
            {
                if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    continue;

                // ── Save file ─────────────────────────────────────────────────
                var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "contracts");
                Directory.CreateDirectory(uploadsDir);
                var fileKey = $"contracts/{Guid.NewGuid()}_{file.FileName}";
                var filePath = Path.Combine(_env.WebRootPath, "uploads", fileKey);

                using (var fs = new FileStream(filePath, FileMode.Create))
                    await file.CopyToAsync(fs);

                // ── Parse PDF ─────────────────────────────────────────────────
                ContractParseResult parsed;
                using (var stream = file.OpenReadStream())
                    parsed = _parser.ParsePdf(stream);

                // ── Log extracted values ─────────────────────────────────────
                _logger.LogInformation(
                    "Contract parsed — File: {File} | Passport1: {P1} | Passport2: {P2} | Transaction: {Txn}",
                    file.FileName,
                    parsed.Passport1 ?? "—",
                    parsed.Passport2 ?? "—",
                    parsed.TransactionNumber ?? "—");

                // ── Try both passports — whichever matches wins ───────────────
                Candidate? matchedCandidate = null;
                string? matchedPassport = null;

                var passportsToTry = new[] { parsed.Passport2, parsed.Passport1 }
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Distinct()
                    .ToList();

                foreach (var passport in passportsToTry)
                {
                    matchedCandidate = await _context.Candidates
                        .Include(c => c.CurrentPackageStage)
                        .FirstOrDefaultAsync(c =>
                            c.PassportNumber != null &&
                            c.PassportNumber.ToUpper() == passport!.ToUpper());

                    if (matchedCandidate != null)
                    {
                        matchedPassport = passport;
                        break;
                    }
                }

                parsed.PassportNumber = matchedPassport ?? parsed.Passport2 ?? parsed.Passport1;

                _logger.LogInformation(
                    "Contract match — Tried: [{Passports}] | Found: {Found} | Candidate: {Name}",
                    string.Join(", ", passportsToTry),
                    matchedCandidate != null,
                    matchedCandidate?.FullName ?? "—");

                var upload = new ContractUpload
                {
                    Id = Guid.NewGuid(),
                    UploadedById = CurrentUserId,
                    FileName = file.FileName,
                    FileKey = fileKey,
                    ExtractedPassportNo = parsed.PassportNumber,
                    ExtractedEmployerName = parsed.EmployerName,
                    ExtractedTransactionNo = parsed.TransactionNumber,
                    MatchStatus = (byte)(matchedCandidate != null ? 1 : 0),
                    MatchedCandidateId = matchedCandidate?.Id,
                    CreatedAt = DateTime.UtcNow
                };

                // Store extraction info in TempData for display
                extractionLog.Add($"📄 {file.FileName} → جواز1: {parsed.Passport1 ?? "—"} | جواز2: {parsed.Passport2 ?? "—"} | جربنا: {string.Join(", ", passportsToTry)} | {(matchedCandidate != null ? $"✅ تطابق: {matchedCandidate.FullName}" : "❌ لا يوجد تطابق")}");

                _context.ContractUploads.Add(upload);

                if (matchedCandidate != null)
                {
                    await ProcessMatchAsync(matchedCandidate, upload, fileKey, file.FileName,
                        (long)file.Length, contractStageIds);
                    autoMatched++;
                }
                else
                {
                    pending++;
                }
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم الرفع: {autoMatched} تطابق تلقائي، {pending} في الانتظار";
            TempData["ExtractionLog"] = string.Join("|", extractionLog);
            return RedirectToAction("Index");
        }

        // ── POST /Contract/ManualMatch ────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ManualMatch(Guid uploadId, Guid candidateId)
        {
            var upload = await _context.ContractUploads.FindAsync(uploadId);
            if (upload == null) return NotFound();

            var candidate = await _context.Candidates
                .Include(c => c.CurrentPackageStage)
                .FirstOrDefaultAsync(c => c.Id == candidateId);
            if (candidate == null) return NotFound();

            var contractStage = await _context.PackageStages
                .Include(ps => ps.StageType)
                .Where(ps => ps.StageType != null && ps.StageType.StageCode == "CONTRACT_ISSUED" && ps.IsActive == true)
                .ToListAsync();
            var contractStageIds = contractStage.Select(s => s.Id).ToHashSet();

            upload.MatchStatus = 2; // Manual
            upload.MatchedCandidateId = candidateId;

            await ProcessMatchAsync(candidate, upload, upload.FileKey, upload.FileName,
                0, contractStageIds);

            await _context.SaveChangesAsync();
            TempData["Success"] = $"تم ربط العقد بـ {candidate.FullName}";
            return RedirectToAction("Index");
        }

        // ── POST /Contract/Dismiss ────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Dismiss(Guid uploadId)
        {
            var upload = await _context.ContractUploads.FindAsync(uploadId);
            if (upload != null)
            {
                upload.MatchStatus = 3;
                await _context.SaveChangesAsync();
            }
            TempData["Success"] = "تم تجاهل الملف";
            return RedirectToAction("Index");
        }

        // ── GET /Contract/SearchCandidates (AJAX) ─────────────────────────────
        [HttpGet]
        public async Task<IActionResult> SearchCandidates(string q)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
                return Json(new List<object>());

            var results = await _context.Candidates
                .Where(c =>
                    c.FullName.Contains(q) ||
                    c.Phone.Contains(q) ||
                    (c.PassportNumber != null && c.PassportNumber.Contains(q)))
                .OrderBy(c => c.FullName)
                .Take(10)
                .Select(c => new
                {
                    c.Id,
                    c.FullName,
                    c.Phone,
                    c.PassportNumber
                })
                .ToListAsync();

            return Json(results);
        }

        // ── Private: process a confirmed match ───────────────────────────────
        private async Task ProcessMatchAsync(
            Candidate candidate,
            ContractUpload upload,
            string fileKey,
            string fileName,
            long fileSize,
            HashSet<Guid> contractStageIds)
        {
            var mimeType = "application/pdf";
            var safeSize = fileSize > 0 ? (int)Math.Min(fileSize, 3145728) : 1;
            var docId = Guid.NewGuid();
            var note = $"عقد عمل رُفع تلقائياً — رقم المعاملة: {upload.ExtractedTransactionNo ?? "—"}";

            // 1. Always save as Document (DocumentType = 3 = Contract)
            _context.Documents.Add(new Document
            {
                Id = docId,
                CandidateId = candidate.Id,
                UploadedById = upload.UploadedById,
                DocumentType = 3,
                FileName = fileName,
                S3key = fileKey,
                FileSizeBytes = safeSize,
                MimeType = mimeType,
                UploadedAt = DateTime.UtcNow
            });

            // 2. Find CONTRACT_ISSUED stage for this candidate's package
            var contractStage = await _context.PackageStages
                .Include(ps => ps.StageType)
                .Where(ps =>
                    ps.PackageId == candidate.JobPackageId &&
                    ps.StageType != null &&
                    ps.StageType.StageCode == "CONTRACT_ISSUED" &&
                    ps.IsActive == true)
                .FirstOrDefaultAsync();

            if (contractStage != null)
            {
                var currentStageOrder = candidate.CurrentPackageStage?.StageOrder ?? 0;
                var contractStageOrder = contractStage.StageOrder;

                if (currentStageOrder <= contractStageOrder)
                {
                    // ── Scenario A: Before OR at CONTRACT_ISSUED
                    //    → Move with IsOverride=true (force jump to CONTRACT_ISSUED)
                    try
                    {
                        var connStr = _context.Database.GetConnectionString()!;
                        using var conn = new SqlConnection(connStr);
                        await conn.OpenAsync();
                        using var cmd = new SqlCommand("demorecruitment.sp_MoveToNextStage", conn)
                        {
                            CommandType = System.Data.CommandType.StoredProcedure
                        };
                        cmd.Parameters.AddWithValue("@CandidateId", candidate.Id);
                        cmd.Parameters.AddWithValue("@ToStageId", contractStage.Id);
                        cmd.Parameters.AddWithValue("@MovedById", upload.UploadedById);
                        cmd.Parameters.AddWithValue("@IsOverride", true);
                        cmd.Parameters.AddWithValue("@Notes", note);
                        await cmd.ExecuteNonQueryAsync();
                    }
                    catch { /* Document already saved — stage move failed silently */ }
                }
                // ── Scenario B: Candidate is AFTER CONTRACT_ISSUED (VISA, DEPARTURE, etc.)
                //    → Document saved only — no stage movement
                //    → Notification will say "عقد متأخر"
            }

            // 3. Determine notification message based on stage position
            string notifTitle, notifBody;
            if (contractStage == null)
            {
                notifTitle = $"عقد عمل جديد — {candidate.FullName}";
                notifBody = $"تم رفع وربط عقد العمل. رقم المعاملة: {upload.ExtractedTransactionNo ?? "—"}";
            }
            else
            {
                var currentOrder = candidate.CurrentPackageStage?.StageOrder ?? 0;
                var contractOrder = contractStage.StageOrder;

                if (currentOrder < contractOrder)
                {
                    // Before CONTRACT_ISSUED — contract arrived early
                    notifTitle = $"📄 عقد عمل مبكر — {candidate.FullName}";
                    notifBody = $"تم رفع عقد العمل قبل وصول المرشح للمرحلة المطلوبة. " +
                                 $"رقم المعاملة: {upload.ExtractedTransactionNo ?? "—"}";
                }
                else if (currentOrder == contractOrder)
                {
                    // At CONTRACT_ISSUED — normal flow
                    notifTitle = $"✅ عقد عمل مكتمل — {candidate.FullName}";
                    notifBody = $"تم رفع وربط عقد العمل وتحريك المرشح للمرحلة التالية. " +
                                 $"رقم المعاملة: {upload.ExtractedTransactionNo ?? "—"}";
                }
                else
                {
                    // After CONTRACT_ISSUED — late contract
                    notifTitle = $"⚠️ عقد عمل متأخر — {candidate.FullName}";
                    notifBody = $"تم رفع عقد العمل بعد تجاوز مرحلة إصدار العقد. " +
                                 $"المرحلة الحالية: {candidate.CurrentPackageStage?.StageName ?? "—"}. " +
                                 $"رقم المعاملة: {upload.ExtractedTransactionNo ?? "—"}";
                }
            }

            var notifiedIds = new HashSet<Guid>();

            // 4. Notify OfficeSales (AssignedSalesId on Candidate = OfficeSales)
            if (candidate.AssignedSalesId != Guid.Empty)
            {
                notifiedIds.Add(candidate.AssignedSalesId);
                _context.Notifications.Add(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = candidate.AssignedSalesId,
                    Type = 1,
                    Title = notifTitle,
                    Body = notifBody,
                    EntityId = candidate.Id,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }

            // 5. Notify Admin
            var adminIds = await _context.Users
                .Where(u => u.Role == 1 && u.IsActive)
                .Select(u => u.Id)
                .ToListAsync();

            foreach (var adminId in adminIds)
            {
                if (notifiedIds.Contains(adminId)) continue;
                _context.Notifications.Add(new Notification
                {
                    Id = Guid.NewGuid(),
                    UserId = adminId,
                    Type = 1,
                    Title = notifTitle,
                    Body = notifBody,
                    EntityId = candidate.Id,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }
    }
}
