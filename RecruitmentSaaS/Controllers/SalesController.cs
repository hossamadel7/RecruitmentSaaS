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
        private readonly RecruitmentSaaS.Services.INotificationService _notifications;

        public SalesController(RecruitmentCrmContext context, IWebHostEnvironment env,
                               RecruitmentSaaS.Services.INotificationService notifications)
        {
            _context = context;
            _env = env;
            _notifications = notifications;
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
            var today = DateOnly.FromDateTime(now);

            var waitingLeads = await _context.Leads
                .Where(l => l.AssignedOfficeSalesId == userId && l.Status == 6)
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

            // ── التذكيرات المتعلقة بالمرشحين لهذا الـ Sales ──────────────────
            var todayReminders = await _context.FollowUpReminders
                .Include(r => r.Candidate)
                .Where(r => r.AssignedToId == userId
                         && r.CandidateId != null
                         && r.Status == 1
                         && r.ReminderDate <= today)
                .OrderBy(r => r.ReminderDate)
                .Select(r => new FollowUpReminderDto
                {
                    Id = r.Id,
                    LeadId = r.LeadId,
                    CandidateId = r.CandidateId,
                    LeadName = r.Candidate != null ? r.Candidate.FullName : "—",
                    LeadPhone = r.Candidate != null ? r.Candidate.Phone : "—",
                    ReminderDate = r.ReminderDate,
                    Status = r.Status,
                    Notes = r.Notes
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

                TodayReminders = todayReminders,
                RecentLeads = waitingLeads
            };

            return View(dto);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DismissCandidateReminder(Guid reminderId, Guid candidateId)
        {
            var reminder = await _context.FollowUpReminders.FindAsync(reminderId);
            if (reminder != null)
            {
                reminder.Status = 3; // Dismissed
                reminder.DismissedAt = DateTime.UtcNow;
                reminder.DismissedById = CurrentUserId;
                reminder.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            TempData["Success"] = "تم تجاهل التذكير";

            // لو جاي من الـ Dashboard يرجع للـ Index، لو جاي من ملف المرشح يرجع ليه
            if (candidateId == Guid.Empty)
                return RedirectToAction("Index");

            return RedirectToAction("CandidateDetail", new { id = candidateId });
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetCandidateReminder(
            Guid candidateId, DateOnly reminderDate, string? notes)
        {
            var userId = CurrentUserId;

            var candidate = await _context.Candidates
                .FirstOrDefaultAsync(c => c.Id == candidateId && c.AssignedSalesId == userId);

            if (candidate == null)
            {
                TempData["Error"] = "المرشح غير موجود";
                return RedirectToAction("Candidates");
            }

            // نحتاج LeadId — نجيبه من الـ Lead المرتبط
            var lead = await _context.Leads
                .FirstOrDefaultAsync(l => l.ConvertedCandidateId == candidateId);

            // لو مفيش lead مرتبط — نستخدم Guid.Empty (LeadId nullable في المنطق بس مش في الـ schema)
            // عشان الـ FK مش nullable نضيف default lead id أو نستخدم lead فعلي
            if (lead == null)
            {
                TempData["Error"] = "لا يمكن إضافة تذكير — لا يوجد عميل مرتبط بهذا المرشح";
                return RedirectToAction("CandidateDetail", new { id = candidateId });
            }

            _context.FollowUpReminders.Add(new FollowUpReminder
            {
                Id = Guid.NewGuid(),
                LeadId = lead.Id,
                CandidateId = candidateId,
                AssignedToId = userId,
                CreatedById = userId,
                ReminderDate = reminderDate,
                Notes = notes,
                Status = 1,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم إضافة تذكير بتاريخ {reminderDate:dd/MM/yyyy} لـ {candidate.FullName}";
            return RedirectToAction("CandidateDetail", new { id = candidateId });
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

                // reload context before touching any entities
                _context.ChangeTracker.Clear();

                // ✅ Commission لا تُنشأ هنا — تُنشأ تلقائياً عند اكتمال الدفع في ApprovePayment

                TempData["Success"] = "تم تحويل العميل إلى مرشح بنجاح";
                return RedirectToAction("CandidateDetail", new { id = newCandidateId });
            }
            catch (Exception ex)
            {
                TempData["Error"] = "حدث خطأ أثناء التحويل: " + (ex.InnerException?.Message ?? ex.Message);
                return RedirectToAction("LeadDetail", new { id = leadId });
            }
        }
        // ── GET /Sales/Candidates ─────────────────────────────────────────────
        public async Task<IActionResult> Candidates(Guid? stageId)
        {
            var userId = CurrentUserId;

            var query = _context.Candidates
                .Include(c => c.JobPackage)
                .Include(c => c.CurrentPackageStage)
                .Where(c => c.AssignedSalesId == userId);

            if (stageId.HasValue)
                query = query.Where(c => c.CurrentPackageStageId == stageId.Value);

            var candidates = await query
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new CandidateListItemDto
                {
                    Id = c.Id,
                    FullName = c.FullName,
                    Phone = c.Phone,
                    NationalId = c.NationalId,
                    CurrentStageName = c.CurrentPackageStage != null ? c.CurrentPackageStage.StageName : "—",
                    CurrentStageOrder = c.CurrentPackageStage != null ? (int)c.CurrentPackageStage.StageOrder : 0,
                    Status = c.Status,
                    JobPackageName = c.JobPackage.Name,
                    TotalPaidEGP = c.TotalPaidEgp,
                    IsProfileComplete = c.IsProfileComplete,
                    IsCompleted = c.IsCompleted,
                    CreatedAt = c.CreatedAt
                })
                .ToListAsync();

            ViewBag.StageId = stageId;
            return View(candidates);
        }

        // ── GET /Sales/CandidateDetail/{id} ──────────────────────────────────
        public async Task<IActionResult> CandidateDetail(Guid id)
        {
            var userId = CurrentUserId;

            var candidate = await _context.Candidates
                .Include(c => c.JobPackage)
                    .ThenInclude(p => p.PackageStages.Where(s => s.IsActive == true).OrderBy(s => s.StageOrder))
                    .ThenInclude(ps => ps.StageType)
                .Include(c => c.RegisteredBy)
                .Include(c => c.CurrentPackageStage)
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

            // Stage action completions
            ViewBag.StageCompletions = await _context.StageActionCompletions
                .Where(sc => sc.CandidateId == id)
                .ToListAsync();

            // Pending approval request
            ViewBag.PendingApproval = await _context.StageApprovalRequests
                .Include(r => r.ToStage)
                .FirstOrDefaultAsync(r => r.CandidateId == id && r.Status == 1);

            var dto = new CandidateDetailDto
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
                FromStageId = candidate.CurrentPackageStageId,
                ToStageId = candidate.CurrentPackageStageId,
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

        // ── POST /Sales/MoveToNextStage ──────────────────────────────────────
        // استبدل ToggleStage القديم بنظام المراحل الجديد
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MoveToNextStage(Guid candidateId, string? notes)
        {
            var userId = CurrentUserId;

            var candidate = await _context.Candidates
                .Include(c => c.CurrentPackageStage)
                .FirstOrDefaultAsync(c => c.Id == candidateId && c.AssignedSalesId == userId);

            if (candidate == null)
            {
                TempData["Error"] = "المرشح غير موجود";
                return RedirectToAction("Candidates");
            }

            // استدعاء الـ SP
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
                new Microsoft.Data.SqlClient.SqlParameter("@MovedById", userId),
                new Microsoft.Data.SqlClient.SqlParameter("@Notes", (object?)notes ?? DBNull.Value),
                new Microsoft.Data.SqlClient.SqlParameter("@IsOverride", false),
                new Microsoft.Data.SqlClient.SqlParameter("@OverrideReason", DBNull.Value),
                successParam, messageParam, stageNameParam
            );

            var success = (bool)successParam.Value;
            var message = messageParam.Value?.ToString() ?? "";

            if (success)
            {
                TempData["Success"] = message;
            }
            else if (message.StartsWith("DOCUMENT_REQUIRED:"))
            {
                // Format: DOCUMENT_REQUIRED:{stageName}
                var parts = message.Split(':', 2);
                TempData["DocumentRequired"] = "1";
                TempData["Error"] = $"يجب رفع المستند المطلوب قبل الانتقال من مرحلة {(parts.Length > 1 ? parts[1] : "")}";
            }
            else if (message.StartsWith("PAYMENT_EXCEPTION_REQUIRED:"))
            {
                // Format: PAYMENT_EXCEPTION_REQUIRED:{fromId}:{toId}:{minPay}:{paid}
                var parts = message.Split(':');
                if (parts.Length == 5)
                {
                    TempData["PaymentException"] = "1";
                    TempData["ExceptionFromStageId"] = parts[1];
                    TempData["ExceptionToStageId"] = parts[2];
                    TempData["ExceptionMinPay"] = parts[3];
                    TempData["ExceptionAmountPaid"] = parts[4];
                }
                TempData["Error"] = $"مطلوب سداد المبلغ المطلوب أولاً — يمكنك طلب استثناء من الأدمن";
            }
            else if (message.StartsWith("REQUIRES_APPROVAL:"))
            {
                var parts = message.Split(':');
                if (parts.Length == 3)
                {
                    TempData["RequiresApproval"] = "1";
                    TempData["ApprovalFromStageId"] = parts[1];
                    TempData["ApprovalToStageId"] = parts[2];
                }
                TempData["Error"] = "هذه المرحلة تحتاج موافقة الأدمن — اضغط طلب الموافقة";
            }
            else
            {
                TempData["Error"] = message;
            }

            candidate.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return RedirectToAction("CandidateDetail", new { id = candidateId });
        }

        // ── POST /Sales/RequestStageApproval ─────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestStageApproval(
            Guid candidateId, Guid fromStageId, Guid toStageId, string? requestNote,
            byte exceptionType = 2, decimal? minPaymentRequired = null, decimal? amountPaid = null)
        {
            var userId = CurrentUserId;

            var candidate = await _context.Candidates
                .FirstOrDefaultAsync(c => c.Id == candidateId && c.AssignedSalesId == userId);

            if (candidate == null)
            {
                TempData["Error"] = "المرشح غير موجود";
                return RedirectToAction("Candidates");
            }

            // Check if pending request already exists
            var existing = await _context.StageApprovalRequests
                .AnyAsync(r => r.CandidateId == candidateId
                    && r.FromStageId == fromStageId
                    && r.Status == 1);

            if (existing)
            {
                TempData["Error"] = "يوجد طلب معلق بالفعل في انتظار موافقة الأدمن";
                return RedirectToAction("CandidateDetail", new { id = candidateId });
            }

            _context.StageApprovalRequests.Add(new StageApprovalRequest
            {
                Id = Guid.NewGuid(),
                CandidateId = candidateId,
                FromStageId = fromStageId,
                ToStageId = toStageId,
                RequestedById = userId,
                RequestedAt = DateTime.UtcNow,
                RequestNote = requestNote,
                Status = 1, // Pending
                ExceptionType = exceptionType,
                MinPaymentRequired = minPaymentRequired,
                AmountPaid = amountPaid ?? candidate.TotalPaidEgp
            });

            await _context.SaveChangesAsync();

            // Notify all admins
            var fromStage = await _context.PackageStages.FindAsync(fromStageId);
            var toStage = await _context.PackageStages.FindAsync(toStageId);
            var cand = await _context.Candidates.FindAsync(candidateId);
            var typeText = exceptionType == 1 ? "استثناء دفع" : "موافقة مرحلة";

            await _notifications.SendToAdminsAsync(
                title: $"طلب {typeText} جديد",
                message: $"{cand?.FullName ?? string.Empty} — من {fromStage?.StageName} إلى {toStage?.StageName}",
                link: "/Admin/StageApprovals",
                type: RecruitmentSaaS.Services.NotificationType.ApprovalRequest
            );

            TempData["Success"] = $"تم إرسال طلب {typeText} للأدمن بنجاح";
            return RedirectToAction("CandidateDetail", new { id = candidateId });
        }

        // ── POST /Sales/CompleteStageAction ───────────────────────────────────
        // يُسجل إتمام الـ action للمرحلة الحالية يدوياً
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompleteStageAction(
            Guid candidateId, Guid packageStageId, string? notes)
        {
            var userId = CurrentUserId;

            var candidate = await _context.Candidates
                .FirstOrDefaultAsync(c => c.Id == candidateId && c.AssignedSalesId == userId);

            if (candidate == null)
            {
                TempData["Error"] = "المرشح غير موجود";
                return RedirectToAction("Candidates");
            }

            // Upsert — إما يضيف أو يحدث لو موجود
            var existing = await _context.StageActionCompletions
                .FirstOrDefaultAsync(s => s.CandidateId == candidateId
                    && s.PackageStageId == packageStageId);

            if (existing == null)
            {
                _context.StageActionCompletions.Add(new StageActionCompletion
                {
                    Id = Guid.NewGuid(),
                    CandidateId = candidateId,
                    PackageStageId = packageStageId,
                    CompletedAt = DateTime.UtcNow,
                    CompletedById = userId,
                    CompletionType = 1, // Manual
                    Notes = notes
                });
            }
            else
            {
                existing.CompletedAt = DateTime.UtcNow;
                existing.CompletedById = userId;
                existing.Notes = notes;
            }

            await _context.SaveChangesAsync();
            TempData["Success"] = "تم تسجيل إتمام المرحلة بنجاح";
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
        public async Task<IActionResult> UploadDocument(Guid candidateId, byte documentType, IFormFile file, string? passportNumber = null, DateOnly? passportExpiry = null)
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

            // Passport fields are mandatory when documentType = 1
            if (documentType == 1)
            {
                if (string.IsNullOrWhiteSpace(passportNumber))
                {
                    TempData["Error"] = "رقم الجواز إجباري عند رفع صورة الجواز";
                    return RedirectToAction("CandidateDetail", new { id = candidateId });
                }
                if (!passportExpiry.HasValue)
                {
                    TempData["Error"] = "تاريخ انتهاء الجواز إجباري عند رفع صورة الجواز";
                    return RedirectToAction("CandidateDetail", new { id = candidateId });
                }
                if (passportExpiry.Value <= DateOnly.FromDateTime(DateTime.Today))
                {
                    TempData["Error"] = "تاريخ انتهاء الجواز يجب أن يكون في المستقبل";
                    return RedirectToAction("CandidateDetail", new { id = candidateId });
                }
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

            // ── If passport — save data to candidate profile ─────────────────
            if (documentType == 1)
            {
                if (!string.IsNullOrWhiteSpace(passportNumber))
                    candidate.PassportNumber = passportNumber.Trim().ToUpper();

                if (passportExpiry.HasValue)
                    candidate.PassportExpiry = passportExpiry.Value;

                // StageActionCompletion is now recorded by sp_MoveToNextStage when candidate passes the stage
                // No manual completion needed here — passport data saved, stage completion happens on transition
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = documentType == 1
                ? "تم رفع الجواز وحفظ البيانات بنجاح ✅"
                : "تم رفع المستند بنجاح";
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
