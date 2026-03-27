using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.DTOs;
using RecruitmentSaaS.Models.Entities;
using System.Data;
using System.IO.Compression;
using System.Security.Claims;

namespace RecruitmentSaaS.Controllers
{
    [Authorize(Roles = "1")]
    public class AdminController : Controller
    {
        private readonly RecruitmentCrmContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly RecruitmentSaaS.Services.INotificationService _notifications;

        public AdminController(RecruitmentCrmContext context, IWebHostEnvironment env,
                               RecruitmentSaaS.Services.INotificationService notifications)
        {
            _context = context;
            _env = env;
            _notifications = notifications;
        }

        // ── Auto-load sidebar badge counts ───────────────────────────────────
        public override void OnActionExecuting(
            Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
        {
            base.OnActionExecuting(context);
            try
            {
                ViewBag.PendingApprovalsCount = _context.StageApprovalRequests.Count(r => r.Status == 1);
                ViewBag.PendingPaymentsCount = _context.Payments.Count(p => p.Status == 1);
                ViewBag.PendingRefundsCount = _context.Refunds.Count(r => r.Status == 1);
                ViewBag.PendingCommissionsCount = _context.Commissions.Count(c => c.Status == 1);
            }
            catch { /* fail silently */ }
        }


        private Guid CurrentUserId =>
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // ── GET /Admin/Index ────────────────────────────────────────────────
        public async Task<IActionResult> Index(int? month, int? year)
        {
            var now = DateTime.UtcNow;
            var selMonth = month ?? now.Month;
            var selYear = year ?? now.Year;
            var monthStart = new DateTime(selYear, selMonth, 1);
            var monthEnd = monthStart.AddMonths(1);

            var totalLeads = await _context.Leads.CountAsync();
            var totalCandidates = await _context.Candidates.CountAsync();
            var totalUsers = await _context.Users.CountAsync();
            var activeCampaigns = await _context.Campaigns.CountAsync(c => c.Status == 1);

            var pendingRefunds = await _context.Refunds.CountAsync(r => r.Status == 1);
            var pendingCommissions = await _context.Commissions.CountAsync(c => c.Status == 1);
            var pendingPayments = await _context.Payments.CountAsync(p => p.Status == 1);

            var totalCollected = await _context.Payments
                .Where(p => p.Status == 2 && p.TransactionType == 1)
                .SumAsync(p => (decimal?)p.AmountEgp) ?? 0;

            // Funnel statuses only: 1,2,4,5,8 + 7(تحويل)
            var funnelStatuses = new byte[] { 1, 2, 4, 5, 8, 7 };

            var leadsThisMonth = await _context.Leads
                .Where(l => l.CreatedAt >= monthStart && l.CreatedAt < monthEnd)
                .GroupBy(l => l.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            // Build ordered list with 0 for missing statuses
            var leadsByStatus = funnelStatuses.Select(s => new
            {
                Status = (int)s,
                Count = leadsThisMonth.FirstOrDefault(x => x.Status == s)?.Count ?? 0
            }).ToList();

            var candidatesByStage = await _context.Candidates
                .Where(c => c.IsCompleted != true
                         && c.CurrentPackageStageId != null
                         && c.CreatedAt >= monthStart
                         && c.CreatedAt < monthEnd)
                .GroupBy(c => c.CurrentPackageStage!.StageName)
                .Select(g => new { Stage = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .ToListAsync();

            var recentLeads = await _context.Leads
                .Include(l => l.Campaign)
                .Include(l => l.AssignedSales)
                .Where(l => l.CreatedAt >= monthStart && l.CreatedAt < monthEnd)
                .OrderByDescending(l => l.CreatedAt)
                .Take(10)
                .ToListAsync();

            var monthLeadsTotal = leadsThisMonth.Sum(x => x.Count);
            var monthCandidatesTotal = candidatesByStage.Sum(x => x.Count);

            ViewBag.TotalLeads = totalLeads;
            ViewBag.TotalCandidates = totalCandidates;
            ViewBag.TotalUsers = totalUsers;
            ViewBag.ActiveCampaigns = activeCampaigns;
            ViewBag.PendingRefunds = pendingRefunds;
            ViewBag.PendingCommissions = pendingCommissions;
            ViewBag.PendingPayments = pendingPayments;
            ViewBag.TotalCollected = totalCollected;
            ViewBag.LeadsByStatus = leadsByStatus;
            ViewBag.CandidatesByStage = candidatesByStage;
            ViewBag.RecentLeads = recentLeads;
            ViewBag.SelectedMonth = selMonth;
            ViewBag.SelectedYear = selYear;
            ViewBag.MonthLeadsTotal = monthLeadsTotal;
            ViewBag.MonthCandidatesTotal = monthCandidatesTotal;

            return View();
        }
        // ── GET /Admin/Users ────────────────────────────────────────────────
        public async Task<IActionResult> Users(string? q, int? role)
        {
            var query = _context.Users.AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(u => u.FullName.Contains(q) || u.Email.Contains(q));

            if (role.HasValue)
                query = query.Where(u => u.Role == role.Value);

            var users = await query.OrderBy(u => u.Role).ThenBy(u => u.FullName).ToListAsync();

            ViewBag.Q = q;
            ViewBag.Role = role;

            return View(users);
        }

        // ── POST /Admin/CreateUser ──────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateUser(string fullName, string email, string password, int role, Guid? branchId)
        {
            if (await _context.Users.AnyAsync(u => u.Email == email))
            {
                TempData["Error"] = "البريد الإلكتروني مستخدم بالفعل";
                return RedirectToAction("Users");
            }

            var user = new User
            {
                Id = Guid.NewGuid(),
                FullName = fullName,
                Email = email,
                PasswordHash = password, // plain text for now
                Role = (byte)role,
                BranchId = branchId ?? Guid.Parse("00000000-0000-0000-0000-000000000020"),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم إنشاء المستخدم {fullName} بنجاح";
            return RedirectToAction("Users");
        }

        // ── POST /Admin/ToggleUser ──────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleUser(Guid userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                TempData["Error"] = "المستخدم غير موجود";
                return RedirectToAction("Users");
            }

            if (user.Id == CurrentUserId)
            {
                TempData["Error"] = "لا يمكنك تعطيل حسابك الخاص";
                return RedirectToAction("Users");
            }

            user.IsActive = !user.IsActive;
            await _context.SaveChangesAsync();

            TempData["Success"] = user.IsActive ? $"تم تفعيل {user.FullName}" : $"تم تعطيل {user.FullName}";
            return RedirectToAction("Users");
        }

        // ── POST /Admin/ResetPassword ───────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(Guid userId, string newPassword)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                TempData["Error"] = "المستخدم غير موجود";
                return RedirectToAction("Users");
            }

            user.PasswordHash = newPassword; // plain text for now
            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم تغيير كلمة المرور لـ {user.FullName}";
            return RedirectToAction("Users");
        }

        // ── GET /Admin/Leads ────────────────────────────────────────────────
        public async Task<IActionResult> Leads(string? q, int? status, Guid? campaignId, int page = 1)
        {
            const int pageSize = 25;

            var query = _context.Leads
                .Include(l => l.Campaign)
                .Include(l => l.AssignedSales)
                .Include(l => l.AssignedOfficeSales)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(l =>
                    l.FullName.Contains(q) ||
                    l.Phone.Contains(q));

            if (status.HasValue)
                query = query.Where(l => l.Status == status.Value);

            if (campaignId.HasValue)
                query = query.Where(l => l.CampaignId == campaignId.Value);

            query = query.OrderByDescending(l => l.CreatedAt);

            var total = await query.CountAsync();
            var leads = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var campaigns = await _context.Campaigns
                .Where(c => c.Status == 1)
                .OrderBy(c => c.Name)
                .ToListAsync();

            ViewBag.Q = q;
            ViewBag.Status = status;
            ViewBag.CampaignId = campaignId;
            ViewBag.Page = page;
            ViewBag.Total = total;
            ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
            ViewBag.Campaigns = campaigns;

            return View(leads);
        }

        // ── GET /Admin/Candidates ───────────────────────────────────────────
        public async Task<IActionResult> Candidates(string? q, Guid? stageId, int page = 1)
        {
            const int pageSize = 25;

            var query = _context.Candidates
                .Include(c => c.JobPackage)
                .Include(c => c.AssignedSales)
                .Include(c => c.CurrentPackageStage)
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

        // ── GET /Admin/Refunds ──────────────────────────────────────────────
        // Admin approves/rejects refund requests (Status 1 → 2 or 3)
        public async Task<IActionResult> Refunds(byte? status, int page = 1)
        {
            const int pageSize = 20;

            var query = _context.Refunds
                .Include(r => r.Candidate)
                    .ThenInclude(c => c.JobPackage)
                .Include(r => r.RequestedBy)
                .Include(r => r.ReviewedBy)
                .Include(r => r.ExecutedBy)
                .AsQueryable();

            // Default: show pending requests (status=1)
            if (status.HasValue)
                query = query.Where(r => r.Status == status.Value);
            else
                query = query.Where(r => r.Status == 1);

            query = query.OrderByDescending(r => r.RequestedAt);

            var total = await query.CountAsync();
            var refunds = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.StatusFilter = status ?? (byte)1;
            ViewBag.Page = page;
            ViewBag.Total = total;
            ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);

            return View(refunds);
        }

        // ── POST /Admin/ApproveRefund ───────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveRefund(Guid refundId)
        {
            var refund = await _context.Refunds
                .FirstOrDefaultAsync(r => r.Id == refundId && r.Status == 1);

            if (refund == null)
            {
                TempData["Error"] = "الطلب غير موجود أو تمت معالجته مسبقاً";
                return RedirectToAction("Refunds");
            }

            refund.Status = 2; // Approved → Accountant can execute
            refund.ReviewedById = CurrentUserId;
            refund.ReviewedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم اعتماد طلب الاسترداد بمبلغ {refund.AmountEgp:N0} ج.م وإحالته للمحاسب";
            return RedirectToAction("Refunds");
        }

        // ── POST /Admin/RejectRefund ────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectRefund(Guid refundId, string rejectReason)
        {
            var refund = await _context.Refunds
                .FirstOrDefaultAsync(r => r.Id == refundId && r.Status == 1);

            if (refund == null)
            {
                TempData["Error"] = "الطلب غير موجود أو تمت معالجته مسبقاً";
                return RedirectToAction("Refunds");
            }

            refund.Status = 3; // Rejected
            refund.ReviewedById = CurrentUserId;
            refund.ReviewedAt = DateTime.UtcNow;
            refund.RejectReason = rejectReason;

            await _context.SaveChangesAsync();

            TempData["Error"] = "تم رفض طلب الاسترداد";
            return RedirectToAction("Refunds");
        }

        // ── GET /Admin/Commissions ──────────────────────────────────────────
        // Admin approves commissions (Status 1 → 2) or reverses (→ 4)
        public async Task<IActionResult> Commissions(byte? status, int page = 1)
        {
            const int pageSize = 20;

            var query = _context.Commissions
                .Include(c => c.SalesUser)
                .Include(c => c.Candidate)
                    .ThenInclude(c => c.JobPackage)
                .Include(c => c.ApprovedBy)
                .Include(c => c.PaidBy)
                .AsQueryable();

            if (status.HasValue)
                query = query.Where(c => c.Status == status.Value);
            else
                query = query.Where(c => c.Status == 1);

            query = query.OrderByDescending(c => c.CreatedAt);

            var total = await query.CountAsync();
            var commissions = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.StatusFilter = status ?? (byte)1;
            ViewBag.Page = page;
            ViewBag.Total = total;
            ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
            ViewBag.CommissionTiers = await _context.CommissionTiers
                .Where(t => t.IsActive)
                .OrderBy(t => t.MinDeals)
                .ToListAsync();

            return View(commissions);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveAllUserCommissions(Guid salesUserId)
        {
            var commissions = await _context.Commissions
                .Where(c => c.SalesUserId == salesUserId && c.Status == 1)
                .ToListAsync();

            if (!commissions.Any())
            {
                TempData["Error"] = "لا توجد عمولات معلقة لهذا الموظف";
                return RedirectToAction("Commissions");
            }

            foreach (var c in commissions)
            {
                c.Status = 2;
                c.ApprovedById = CurrentUserId;
                c.ApprovedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            var salesUser = await _context.Users.FindAsync(salesUserId);
            TempData["Success"] = $"تم اعتماد {commissions.Count} عمولة لـ {salesUser?.FullName}";
            return RedirectToAction("Commissions");
        }
        // ── POST /Admin/ApproveCommission ───────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveCommission(Guid commissionId)
        {
            var commission = await _context.Commissions
                .FirstOrDefaultAsync(c => c.Id == commissionId && c.Status == 1);

            if (commission == null)
            {
                TempData["Error"] = "العمولة غير موجودة أو تمت معالجتها مسبقاً";
                return RedirectToAction("Commissions");
            }

            commission.Status = 2; // Approved → Accountant can pay
            commission.ApprovedById = CurrentUserId;
            commission.ApprovedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم اعتماد العمولة بمبلغ {commission.AmountEgp:N0} ج.م وإحالتها للمحاسب";
            return RedirectToAction("Commissions");
        }

        // ── POST /Admin/RejectCommission ────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectCommission(Guid commissionId, string reversedReason)
        {
            var commission = await _context.Commissions
                .FirstOrDefaultAsync(c => c.Id == commissionId && c.Status == 1);

            if (commission == null)
            {
                TempData["Error"] = "العمولة غير موجودة أو تمت معالجتها مسبقاً";
                return RedirectToAction("Commissions");
            }

            commission.Status = 4; // Reversed
            commission.ReversedReason = reversedReason;

            await _context.SaveChangesAsync();

            TempData["Error"] = "تم رفض العمولة";
            return RedirectToAction("Commissions");
        }

        // ── GET /Admin/Branches ─────────────────────────────────────────────
        public async Task<IActionResult> Branches()
        {
            var branches = await _context.Branches
                .OrderBy(b => b.Name)
                .ToListAsync();

            return View(branches);
        }

        // ── GET /Admin/Campaigns ────────────────────────────────────────────
        public async Task<IActionResult> Campaigns(int page = 1)
        {
            const int pageSize = 20;

            var query = _context.Campaigns
                .OrderByDescending(c => c.CreatedAt);

            var total = await query.CountAsync();
            var campaigns = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.Page = page;
            ViewBag.Total = total;
            ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);

            return View(campaigns);
        }

        // ── POST /Admin/ToggleCampaign ──────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleCampaign(Guid campaignId)
        {
            var campaign = await _context.Campaigns.FindAsync(campaignId);
            if (campaign == null)
            {
                TempData["Error"] = "الحملة غير موجودة";
                return RedirectToAction("Campaigns");
            }

            campaign.Status = campaign.Status == 1 ? (byte)0 : (byte)1;
            await _context.SaveChangesAsync();

            TempData["Success"] = campaign.Status == 1 ? "تم تفعيل الحملة" : "تم إيقاف الحملة";
            return RedirectToAction("Campaigns");
        }


        // ── GET /Admin/CreatePackage ─────────────────────────────────────────
        public IActionResult CreatePackage()
        {
            return View();
        }

        // ── POST /Admin/CreatePackage ────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreatePackage(
            string name, string destinationCountry, string jobTitle, decimal priceEgp, string? description)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                TempData["Error"] = "اسم الباقة مطلوب";
                return RedirectToAction("Packages");
            }

            var pkg = new JobPackage
            {
                Id = Guid.NewGuid(),
                Name = name.Trim(),
                DestinationCountry = destinationCountry?.Trim() ?? "",
                JobTitle = jobTitle?.Trim() ?? "",
                PriceEgp = priceEgp,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            _context.JobPackages.Add(pkg);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم إنشاء الباقة \"{name}\" بنجاح";
            return RedirectToAction("PackageStages", new { packageId = pkg.Id });
        }

        // ── POST /Admin/EditPackage ──────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditPackage(
            Guid packageId, string name, string destinationCountry,
            string jobTitle, decimal priceEgp, string? description)
        {
            var pkg = await _context.JobPackages.FindAsync(packageId);
            if (pkg == null)
            {
                TempData["Error"] = "الباقة غير موجودة";
                return RedirectToAction("Packages");
            }

            pkg.Name = name.Trim();
            pkg.DestinationCountry = destinationCountry?.Trim() ?? "";
            pkg.JobTitle = jobTitle?.Trim() ?? "";
            pkg.PriceEgp = priceEgp;

            await _context.SaveChangesAsync();
            TempData["Success"] = "تم تعديل الباقة بنجاح";
            return RedirectToAction("PackageStages", new { packageId = pkg.Id });
        }

        // ── POST /Admin/TogglePackage ────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TogglePackage(Guid packageId)
        {
            var pkg = await _context.JobPackages.FindAsync(packageId);
            if (pkg == null)
            {
                TempData["Error"] = "الباقة غير موجودة";
                return RedirectToAction("Packages");
            }

            pkg.IsActive = !pkg.IsActive;
            await _context.SaveChangesAsync();
            TempData["Success"] = pkg.IsActive ? "تم تفعيل الباقة" : "تم إيقاف الباقة";
            return RedirectToAction("Packages");
        }

        // ── GET /Admin/StageApprovals ────────────────────────────────────────
        public async Task<IActionResult> StageApprovals(byte? status)
        {
            var query = _context.StageApprovalRequests
                .Include(r => r.Candidate)
                    .ThenInclude(c => c.JobPackage)
                .Include(r => r.FromStage)
                .Include(r => r.ToStage)
                .Include(r => r.RequestedBy)
                .Include(r => r.ReviewedBy)
                .AsQueryable();

            if (status.HasValue)
                query = query.Where(r => r.Status == status.Value);
            else
                query = query.Where(r => r.Status == 1); // Pending by default

            var requests = await query
                .OrderByDescending(r => r.RequestedAt)
                .ToListAsync();

            ViewBag.StatusFilter = status ?? (byte)1;
            ViewBag.PendingCount = await _context.StageApprovalRequests
                .CountAsync(r => r.Status == 1);

            return View(requests);
        }

        // ── POST /Admin/ApproveStageRequest ──────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveStageRequest(Guid requestId, string? adminNote)
        {
            var request = await _context.StageApprovalRequests
                .Include(r => r.Candidate)
                .Include(r => r.FromStage)
                .Include(r => r.ToStage)
                .FirstOrDefaultAsync(r => r.Id == requestId && r.Status == 1);

            if (request == null)
            {
                TempData["Error"] = "الطلب غير موجود أو تمت معالجته";
                return RedirectToAction("StageApprovals");
            }

            // Move candidate to next stage with override
            var spSuccess = new SqlParameter { ParameterName = "@Success", SqlDbType = System.Data.SqlDbType.Bit, Direction = System.Data.ParameterDirection.Output };
            var spMessage = new SqlParameter { ParameterName = "@Message", SqlDbType = System.Data.SqlDbType.NVarChar, Size = 500, Direction = System.Data.ParameterDirection.Output };
            var spStage = new SqlParameter { ParameterName = "@NewStageName", SqlDbType = System.Data.SqlDbType.NVarChar, Size = 200, Direction = System.Data.ParameterDirection.Output };

            await _context.Database.ExecuteSqlRawAsync(
                "EXEC demorecruitment.sp_MoveToNextStage @CandidateId, @MovedById, @Notes, @IsOverride, @OverrideReason, @Success OUTPUT, @Message OUTPUT, @NewStageName OUTPUT",
                new SqlParameter("@CandidateId", request.CandidateId),
                new SqlParameter("@MovedById", CurrentUserId),
                new SqlParameter("@Notes", (object?)adminNote ?? DBNull.Value),
                new SqlParameter("@IsOverride", true),  // Override — admin approved
                new SqlParameter("@OverrideReason", (object?)adminNote ?? DBNull.Value),
                spSuccess, spMessage, spStage
            );

            // Update request status
            request.Status = 2; // Approved
            request.ReviewedById = CurrentUserId;
            request.ReviewedAt = DateTime.UtcNow;
            request.AdminNote = adminNote;
            await _context.SaveChangesAsync();

            var stageName = spStage.Value?.ToString() ?? request.ToStage?.StageName ?? "";

            // Notify the requester (Sales user)
            await _notifications.SendAsync(
                userId: request.RequestedById,
                title: "تمت الموافقة على طلبك ✅",
                message: $"تمت الموافقة على انتقال {request.Candidate?.FullName ?? string.Empty} إلى {stageName}",
                link: $"/Sales/CandidateDetail/{request.CandidateId}",
                type: RecruitmentSaaS.Services.NotificationType.ApprovalRequest
            );

            TempData["Success"] = $"تمت الموافقة — انتقل المرشح إلى \"{stageName}\"";
            return RedirectToAction("StageApprovals");
        }

        // ── POST /Admin/RejectStageRequest ───────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectStageRequest(Guid requestId, string adminNote)
        {
            var request = await _context.StageApprovalRequests
                .FirstOrDefaultAsync(r => r.Id == requestId && r.Status == 1);

            if (request == null)
            {
                TempData["Error"] = "الطلب غير موجود أو تمت معالجته";
                return RedirectToAction("StageApprovals");
            }

            request.Status = 3; // Rejected
            request.ReviewedById = CurrentUserId;
            request.ReviewedAt = DateTime.UtcNow;
            request.AdminNote = adminNote;
            await _context.SaveChangesAsync();

            // Notify the requester (Sales user)
            await _notifications.SendAsync(
                userId: request.RequestedById,
                title: "تم رفض طلبك ❌",
                message: $"تم رفض طلب الانتقال — {adminNote}",
                link: $"/Sales/CandidateDetail/{request.CandidateId}",
                type: RecruitmentSaaS.Services.NotificationType.ApprovalRequest
            );

            TempData["Error"] = "تم رفض طلب الانتقال";
            return RedirectToAction("StageApprovals");
        }

        // ── GET /Admin/Packages ──────────────────────────────────────────────
        public async Task<IActionResult> Packages()
        {
            var packages = await _context.JobPackages
                .Include(p => p.PackageStages.Where(s => s.IsActive == true).OrderBy(s => s.StageOrder))
                .Where(p => p.IsActive == true)
                .OrderBy(p => p.Name)
                .ToListAsync();

            return View(packages);
        }

        // ── GET /Admin/PackageStages/{packageId} ─────────────────────────────
        public async Task<IActionResult> PackageStages(Guid? packageId, Guid? id)
        {
            var pkgId = packageId ?? id;
            if (pkgId == null)
            {
                TempData["Error"] = "الباقة غير موجودة";
                return RedirectToAction("Packages");
            }

            var package = await _context.JobPackages
                .Include(p => p.PackageStages.OrderBy(s => s.StageOrder))
                .FirstOrDefaultAsync(p => p.Id == pkgId.Value);

            if (package == null)
            {
                TempData["Error"] = "الباقة غير موجودة";
                return RedirectToAction("Packages");
            }

            // Load StageTypes for dropdown in Add/Edit stage modals
            ViewBag.StageTypes = await _context.StageTypes
                .Where(st => st.IsActive == true)
                .OrderBy(st => st.SortOrder)
                .ToListAsync();

            return View(package);
        }

        // ── POST /Admin/AddPackageStage ──────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddPackageStage(
            Guid packageId, Guid? stageTypeId, string? stageName, int stageOrder,
            decimal? requiredMinPaymentEgp, string? description, bool notifySalesOnEnter = false)
        {
            // If stageTypeId selected, use its name
            string finalName = stageName?.Trim() ?? "";
            if (stageTypeId.HasValue)
            {
                var stageType = await _context.StageTypes.FindAsync(stageTypeId.Value);
                if (stageType != null && string.IsNullOrWhiteSpace(finalName))
                    finalName = stageType.Name;
            }

            if (string.IsNullOrWhiteSpace(finalName))
            {
                TempData["Error"] = "اسم المرحلة مطلوب";
                return RedirectToAction("PackageStages", new { packageId });
            }

            var orderExists = await _context.PackageStages
                .AnyAsync(s => s.PackageId == packageId && s.StageOrder == stageOrder);

            if (orderExists)
            {
                TempData["Error"] = $"يوجد مرحلة بالترتيب رقم {stageOrder} بالفعل";
                return RedirectToAction("PackageStages", new { packageId });
            }

            _context.PackageStages.Add(new PackageStage
            {
                Id = Guid.NewGuid(),
                PackageId = packageId,
                StageTypeId = stageTypeId,
                StageName = finalName,
                StageOrder = stageOrder,
                RequiredMinPaymentEgp = requiredMinPaymentEgp,
                Description = description?.Trim(),
                NotifySalesOnEnter = notifySalesOnEnter,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
            TempData["Success"] = $"تم إضافة مرحلة \"{finalName}\" بنجاح";
            return RedirectToAction("PackageStages", new { packageId });
        }

        // ── POST /Admin/EditPackageStage ─────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditPackageStage(
            Guid stageId, Guid packageId, string stageName, int stageOrder,
            decimal? requiredMinPaymentEgp, string? description, bool notifySalesOnEnter = false)
        {
            var stage = await _context.PackageStages.FindAsync(stageId);
            if (stage == null)
            {
                TempData["Error"] = "المرحلة غير موجودة";
                return RedirectToAction("PackageStages", new { packageId });
            }

            var orderExists = await _context.PackageStages
                .AnyAsync(s => s.PackageId == packageId && s.StageOrder == stageOrder && s.Id != stageId);

            if (orderExists)
            {
                TempData["Error"] = $"يوجد مرحلة بالترتيب رقم {stageOrder} بالفعل";
                return RedirectToAction("PackageStages", new { packageId });
            }

            stage.StageName = stageName.Trim();
            stage.StageOrder = stageOrder;
            stage.RequiredMinPaymentEgp = requiredMinPaymentEgp;
            stage.Description = description?.Trim();
            stage.NotifySalesOnEnter = notifySalesOnEnter;

            await _context.SaveChangesAsync();
            TempData["Success"] = $"تم تعديل المرحلة \"{stageName}\" بنجاح";
            return RedirectToAction("PackageStages", new { packageId });
        }

        // ── POST /Admin/TogglePackageStage ───────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TogglePackageStage(Guid stageId, Guid packageId)
        {
            var stage = await _context.PackageStages.FindAsync(stageId);
            if (stage == null)
            {
                TempData["Error"] = "المرحلة غير موجودة";
                return RedirectToAction("PackageStages", new { packageId });
            }

            var candidatesOnStage = await _context.Candidates
                .CountAsync(c => c.CurrentPackageStageId == stageId);

            if (candidatesOnStage > 0 && stage.IsActive == true)
            {
                TempData["Error"] = $"لا يمكن إيقاف هذه المرحلة — يوجد {candidatesOnStage} مرشح عليها حالياً";
                return RedirectToAction("PackageStages", new { packageId });
            }

            stage.IsActive = !stage.IsActive;
            await _context.SaveChangesAsync();
            TempData["Success"] = stage.IsActive == true ? "تم تفعيل المرحلة" : "تم إيقاف المرحلة";
            return RedirectToAction("PackageStages", new { packageId });
        }

        // ── POST /Admin/DeletePackageStage ───────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePackageStage(Guid stageId, Guid packageId)
        {
            var stage = await _context.PackageStages.FindAsync(stageId);
            if (stage == null)
            {
                TempData["Error"] = "المرحلة غير موجودة";
                return RedirectToAction("PackageStages", new { packageId });
            }

            var hasHistory = await _context.CandidateStageHistories
                .AnyAsync(h => h.ToStageId == stageId || h.FromStageId == stageId);

            var hasCandidates = await _context.Candidates
                .AnyAsync(c => c.CurrentPackageStageId == stageId);

            if (hasHistory || hasCandidates)
            {
                TempData["Error"] = "لا يمكن حذف هذه المرحلة لأن لها سجلات مرتبطة — قم بإيقافها بدلاً من الحذف";
                return RedirectToAction("PackageStages", new { packageId });
            }

            _context.PackageStages.Remove(stage);
            await _context.SaveChangesAsync();
            TempData["Success"] = "تم حذف المرحلة بنجاح";
            return RedirectToAction("PackageStages", new { packageId });
        }

        // ── POST /Admin/ReorderStages (JSON) ─────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> ReorderStages([FromBody] List<StageOrderDto> orders)
        {
            if (orders == null || !orders.Any()) return BadRequest();

            foreach (var item in orders)
            {
                var stage = await _context.PackageStages.FindAsync(item.StageId);
                if (stage != null) stage.StageOrder = item.Order;
            }

            await _context.SaveChangesAsync();
            return Ok(new { success = true });
        }

        // ── GET /Admin/CandidateDetail/{id} ──────────────────────────────────
        // Admin can view any candidate regardless of assignment
        public async Task<IActionResult> CandidateDetail(Guid id)
        {
            var candidate = await _context.Candidates
                .Include(c => c.JobPackage)
                    .ThenInclude(p => p.PackageStages.Where(s => s.IsActive == true).OrderBy(s => s.StageOrder))
                .Include(c => c.RegisteredBy)
                .Include(c => c.CurrentPackageStage)
                .FirstOrDefaultAsync(c => c.Id == id);  // No AssignedSalesId filter — Admin sees all

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
            ViewBag.StageCompletions = await _context.StageActionCompletions
                .Where(sc => sc.CandidateId == id)
                .ToListAsync();
            ViewBag.PendingApproval = await _context.StageApprovalRequests
                .Include(r => r.ToStage)
                .FirstOrDefaultAsync(r => r.CandidateId == id && r.Status == 1);

            var dto = new RecruitmentSaaS.Models.DTOs.CandidateDetailDto
            {
                Id = candidate.Id,
                FullName = candidate.FullName,
                Phone = candidate.Phone,
                NationalId = candidate.NationalId,
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

            return View("~/Views/Sales/CandidateDetail.cshtml", dto);
        }

        // ── GET /Admin/DownloadDocument/{id} ────────────────────────────────
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

        // ── POST /Admin/ConfirmPassportSent ──────────────────────────────────
        // Admin يأكد إرسال جواز مرشح معين للوكيل
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmPassportSent(Guid candidateId, Guid packageId)
        {
            var candidate = await _context.Candidates
                .Include(c => c.CurrentPackageStage)
                    .ThenInclude(s => s!.StageType)
                .Include(c => c.JobPackage)
                    .ThenInclude(p => p.PackageStages.OrderBy(s => s.StageOrder))
                        .ThenInclude(s => s.StageType)
                .FirstOrDefaultAsync(c => c.Id == candidateId);

            if (candidate == null)
                return Json(new { success = false, message = "المرشح غير موجود" });

            // Find next stage with PASSPORT_SEND code
            var currentOrder = candidate.CurrentPackageStage?.StageOrder ?? 0;
            var passportSendStage = candidate.JobPackage.PackageStages
                .Where(s => s.StageOrder > currentOrder
                    && s.StageType != null
                    && s.StageType.StageCode == "PASSPORT_SEND"
                    && s.IsActive == true)
                .OrderBy(s => s.StageOrder)
                .FirstOrDefault();

            // If already confirmed (already past PASSPORT_SEND stage)
            var alreadyConfirmed = candidate.JobPackage.PackageStages
                .Any(s => s.StageType != null
                    && s.StageType.StageCode == "PASSPORT_SEND"
                    && s.StageOrder <= currentOrder);

            if (alreadyConfirmed)
                return Json(new { success = true, message = "تم التأكيد مسبقاً", alreadyDone = true });

            if (passportSendStage == null)
                return Json(new { success = false, message = "لا توجد مرحلة إرسال جواز في هذه الباقة" });

            // Call sp_MoveToNextStage
            var successParam = new SqlParameter { ParameterName = "@Success", SqlDbType = SqlDbType.Bit, Direction = ParameterDirection.Output };
            var messageParam = new SqlParameter { ParameterName = "@Message", SqlDbType = SqlDbType.NVarChar, Size = 500, Direction = ParameterDirection.Output };
            var stageParam = new SqlParameter { ParameterName = "@NewStageName", SqlDbType = SqlDbType.NVarChar, Size = 200, Direction = ParameterDirection.Output };

            await _context.Database.ExecuteSqlRawAsync(
                "EXEC demorecruitment.sp_MoveToNextStage @CandidateId, @MovedById, @Notes, @IsOverride, @OverrideReason, @Success OUTPUT, @Message OUTPUT, @NewStageName OUTPUT",
                new SqlParameter("@CandidateId", candidateId),
                new SqlParameter("@MovedById", CurrentUserId),
                new SqlParameter("@Notes", (object?)"تم إرسال الجواز للوكيل" ?? DBNull.Value),
                new SqlParameter("@IsOverride", true),   // override to jump to PASSPORT_SEND
                new SqlParameter("@OverrideReason", "Admin confirmed passport sent"),
                successParam, messageParam, stageParam
            );

            var success = (bool)successParam.Value;
            var message = messageParam.Value?.ToString() ?? "";

            return Json(new { success, message, alreadyDone = false });
        }

        // ── GET /Admin/PackagePassports/{packageId} ──────────────────────────
        public async Task<IActionResult> PackagePassports(Guid? packageId, Guid? id)
        {
            var pkgId = packageId ?? id;
            if (pkgId == null) return NotFound();

            var package = await _context.JobPackages
                .Include(p => p.PackageStages.OrderBy(s => s.StageOrder))
                    .ThenInclude(ps => ps.StageType)
                .FirstOrDefaultAsync(p => p.Id == pkgId.Value);
            if (package == null) return NotFound();

            // PASSPORT_SUBMIT stage IDs for this package — StageCode only, not name
            var passportStageIds = package.PackageStages
                .Where(ps => ps.StageType?.StageCode == "PASSPORT_SUBMIT")
                .Select(ps => ps.Id).ToList();

            // All candidates who have a StageActionCompletion for PASSPORT_SUBMIT
            // Include CompletionType 4 (DocumentUploaded) and 5 (StagePassed)
            var completedCandidateIds = await _context.StageActionCompletions
                .Where(sc => passportStageIds.Contains(sc.PackageStageId))
                .Select(sc => sc.CandidateId).Distinct().ToListAsync();

            // Downloaded map: CandidateId → LogId + DownloadedAt
            var downloadedMap = await _context.PassportDownloadedCandidates
                .Include(pdc => pdc.Log)
                .Where(pdc => pdc.Log.PackageId == pkgId.Value)
                .Select(pdc => new { pdc.CandidateId, pdc.LogId, pdc.Log.DownloadedAt })
                .ToListAsync();

            var downloadedCandidateIds = downloadedMap.Select(d => d.CandidateId).ToList();

            // All eligible candidates (passed stage + has passport doc + moved past stage)
            var allEligible = await _context.Candidates
                .Include(c => c.Documents.Where(d => d.DocumentType == 1))
                .Where(c => c.JobPackageId == pkgId.Value
                    && completedCandidateIds.Contains(c.Id)
                    && c.Documents.Any(d => d.DocumentType == 1)
                    && !passportStageIds.Contains(c.CurrentPackageStageId ?? Guid.Empty))
                .OrderBy(c => c.FullName)
                .Select(c => new
                {
                    c.Id,
                    c.FullName,
                    c.PassportNumber,
                    IsDownloaded = downloadedCandidateIds.Contains(c.Id),
                    PassportDoc = c.Documents
                        .Where(d => d.DocumentType == 1)
                        .OrderByDescending(d => d.UploadedAt)
                        .FirstOrDefault()
                })
                .ToListAsync();

            // New = not yet downloaded
            var newCandidates = allEligible.Where(c => !c.IsDownloaded).ToList();

            // Download history grouped by batch (log)
            var downloadLogs = await _context.PassportDownloadLogs
                .Include(l => l.DownloadedBy)
                .Where(l => l.PackageId == pkgId.Value)
                .OrderByDescending(l => l.DownloadedAt)
                .ToListAsync();

            var batches = downloadLogs.Select(log => new
            {
                Log = log,
                Candidates = allEligible
                    .Where(c => downloadedMap.Any(d => d.CandidateId == c.Id && d.LogId == log.Id))
                    .ToList()
            }).ToList();

            ViewBag.Package = package;
            ViewBag.AllCandidates = allEligible;
            ViewBag.NewCandidates = newCandidates;
            ViewBag.Batches = batches;
            return View();
        }

        // ── GET /Admin/RedownloadPassportsBatch/{logId} ───────────────────────
        public async Task<IActionResult> RedownloadPassportsBatch(Guid logId)
        {
            var log = await _context.PassportDownloadLogs
                .Include(l => l.Package)
                .FirstOrDefaultAsync(l => l.Id == logId);
            if (log == null) return NotFound();

            var candidateIds = await _context.PassportDownloadedCandidates
                .Where(pdc => pdc.LogId == logId)
                .Select(pdc => pdc.CandidateId)
                .ToListAsync();

            var candidates = await _context.Candidates
                .Include(c => c.Documents.Where(d => d.DocumentType == 1))
                .Where(c => candidateIds.Contains(c.Id))
                .ToListAsync();

            if (!candidates.Any())
            {
                TempData["Error"] = "لا يوجد جوازات في هذه الدفعة";
                return RedirectToAction("PackagePassports", new { packageId = log.PackageId });
            }

            using var memoryStream = new MemoryStream();
            int fileCount = 0;

            using (var zip = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
            {
                foreach (var candidate in candidates)
                {
                    var passportDoc = candidate.Documents
                        .Where(d => d.DocumentType == 1)
                        .OrderByDescending(d => d.UploadedAt)
                        .FirstOrDefault();

                    if (passportDoc == null) continue;

                    var filePath = Path.Combine(
                        _env.WebRootPath, "uploads", "candidates",
                        candidate.Id.ToString(),
                        Path.GetFileName(passportDoc.S3key));

                    if (!System.IO.File.Exists(filePath)) continue;

                    var ext = Path.GetExtension(passportDoc.FileName);
                    var passNo = candidate.PassportNumber ?? "unknown";
                    var safeName = string.Concat(candidate.FullName
                                       .Split(Path.GetInvalidFileNameChars()))
                                       .Replace(" ", "_");
                    var entry2 = zip.CreateEntry($"{safeName}_{passNo}{ext}", CompressionLevel.Optimal);
                    using var es2 = entry2.Open();
                    using var fs2 = System.IO.File.OpenRead(filePath);
                    await fs2.CopyToAsync(es2);
                    fileCount++;
                }
            }

            if (fileCount == 0)
            {
                TempData["Error"] = "ملفات الجوازات غير موجودة على السيرفر";
                return RedirectToAction("PackagePassports", new { packageId = log.PackageId });
            }

            memoryStream.Seek(0, SeekOrigin.Begin);
            var safePkgName = string.Concat(log.Package.Name
                                .Split(Path.GetInvalidFileNameChars()))
                                .Replace(" ", "_");
            var zipName = $"{safePkgName}_batch_{log.DownloadedAt:yyyyMMdd_HHmm}.zip";
            return File(memoryStream.ToArray(), "application/zip", zipName);
        }

        // ============================================================
        // ACTIONS — paste inside AdminController class
        // ============================================================

        // GET /Admin/CommissionTiers
        public async Task<IActionResult> CommissionTiers()
        {
            var tiers = await _context.CommissionTiers
                .Where(t => t.IsActive)
                .OrderBy(t => t.MinDeals)
                .Select(t => new CommissionTierDto
                {
                    Id = t.Id,
                    MinDeals = t.MinDeals,
                    MaxDeals = t.MaxDeals,
                    AmountPerDeal = t.AmountPerDeal,
                    IsActive = t.IsActive
                })
                .ToListAsync();

            // Validation: check for overlapping ranges
            ViewBag.HasOverlap = false;
            for (int i = 0; i < tiers.Count - 1; i++)
            {
                var current = tiers[i];
                var next = tiers[i + 1];
                if (current.MaxDeals.HasValue && current.MaxDeals >= next.MinDeals)
                {
                    ViewBag.HasOverlap = true;
                    break;
                }
            }

            // Check for gap between tiers
            ViewBag.HasGap = false;
            for (int i = 0; i < tiers.Count - 1; i++)
            {
                var current = tiers[i];
                var next = tiers[i + 1];
                if (current.MaxDeals.HasValue && current.MaxDeals + 1 < next.MinDeals)
                {
                    ViewBag.HasGap = true;
                    break;
                }
            }

            // Pending commissions count for badge
            ViewBag.PendingCount = await _context.Commissions
                .CountAsync(c => c.Status == 1);

            return View(tiers);
        }

        // POST /Admin/AddCommissionTier
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddCommissionTier(
            int minDeals, int? maxDeals, decimal amountPerDeal)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            // Validate
            if (minDeals < 1)
            {
                TempData["Error"] = "الحد الأدنى للصفقات يجب أن يكون 1 على الأقل";
                return RedirectToAction("CommissionTiers");
            }
            if (amountPerDeal <= 0)
            {
                TempData["Error"] = "المبلغ يجب أن يكون أكبر من صفر";
                return RedirectToAction("CommissionTiers");
            }
            if (maxDeals.HasValue && maxDeals < minDeals)
            {
                TempData["Error"] = "الحد الأقصى يجب أن يكون أكبر من أو يساوي الحد الأدنى";
                return RedirectToAction("CommissionTiers");
            }

            // Check overlap with existing tiers
            var overlapping = await _context.CommissionTiers
                .Where(t => t.IsActive)
                .AnyAsync(t =>
                    t.MinDeals <= (maxDeals ?? int.MaxValue) &&
                    (t.MaxDeals == null || t.MaxDeals >= minDeals));

            if (overlapping)
            {
                TempData["Error"] = "هذا النطاق يتداخل مع شريحة موجودة. يرجى مراجعة النطاقات الحالية";
                return RedirectToAction("CommissionTiers");
            }

            // Check: only one tier can have MaxDeals = null (unlimited)
            if (!maxDeals.HasValue)
            {
                var hasUnlimited = await _context.CommissionTiers
                    .AnyAsync(t => t.IsActive && t.MaxDeals == null);
                if (hasUnlimited)
                {
                    TempData["Error"] = "يوجد بالفعل شريحة مفتوحة النهاية (بلا حد أقصى). يمكن أن تكون شريحة واحدة فقط بلا حد أقصى";
                    return RedirectToAction("CommissionTiers");
                }
            }

            _context.CommissionTiers.Add(new CommissionTier
            {
                Id = Guid.NewGuid(),
                MinDeals = minDeals,
                MaxDeals = maxDeals,
                AmountPerDeal = amountPerDeal,
                IsActive = true,
                CreatedById = userId,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
            TempData["Success"] = $"تمت إضافة الشريحة بنجاح — {minDeals}{(maxDeals.HasValue ? $" إلى {maxDeals}" : "+")} صفقة = {amountPerDeal:N0} ج.م";
            return RedirectToAction("CommissionTiers");
        }

        // POST /Admin/EditCommissionTier
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditCommissionTier(
            Guid id, int minDeals, int? maxDeals, decimal amountPerDeal)
        {
            var tier = await _context.CommissionTiers.FindAsync(id);
            if (tier == null) return NotFound();

            if (minDeals < 1)
            {
                TempData["Error"] = "الحد الأدنى للصفقات يجب أن يكون 1 على الأقل";
                return RedirectToAction("CommissionTiers");
            }
            if (amountPerDeal <= 0)
            {
                TempData["Error"] = "المبلغ يجب أن يكون أكبر من صفر";
                return RedirectToAction("CommissionTiers");
            }
            if (maxDeals.HasValue && maxDeals < minDeals)
            {
                TempData["Error"] = "الحد الأقصى يجب أن يكون أكبر من أو يساوي الحد الأدنى";
                return RedirectToAction("CommissionTiers");
            }

            // Check overlap with OTHER tiers (exclude current)
            var overlapping = await _context.CommissionTiers
                .Where(t => t.IsActive && t.Id != id)
                .AnyAsync(t =>
                    t.MinDeals <= (maxDeals ?? int.MaxValue) &&
                    (t.MaxDeals == null || t.MaxDeals >= minDeals));

            if (overlapping)
            {
                TempData["Error"] = "هذا النطاق يتداخل مع شريحة أخرى موجودة";
                return RedirectToAction("CommissionTiers");
            }

            // Check unlimited constraint
            if (!maxDeals.HasValue && tier.MaxDeals.HasValue)
            {
                var hasUnlimited = await _context.CommissionTiers
                    .AnyAsync(t => t.IsActive && t.MaxDeals == null && t.Id != id);
                if (hasUnlimited)
                {
                    TempData["Error"] = "يوجد بالفعل شريحة مفتوحة النهاية";
                    return RedirectToAction("CommissionTiers");
                }
            }

            tier.MinDeals = minDeals;
            tier.MaxDeals = maxDeals;
            tier.AmountPerDeal = amountPerDeal;
            tier.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            TempData["Success"] = "تم تعديل الشريحة بنجاح";
            return RedirectToAction("CommissionTiers");
        }

        // POST /Admin/DeleteCommissionTier
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteCommissionTier(Guid id)
        {
            var tier = await _context.CommissionTiers.FindAsync(id);
            if (tier == null) return NotFound();

            // Soft delete — don't actually delete, just deactivate
            tier.IsActive = false;
            tier.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            TempData["Success"] = "تم حذف الشريحة بنجاح";
            return RedirectToAction("CommissionTiers");
        }

        // GET /Admin/RecalculateCommissions
        // Recalculates all PENDING commissions for current month based on current tiers
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecalculateCommissions()
        {
            var now = DateTime.UtcNow;
            var month = now.Month;
            var year = now.Year;

            // Get all active tiers ordered
            var tiers = await _context.CommissionTiers
                .Where(t => t.IsActive)
                .OrderBy(t => t.MinDeals)
                .ToListAsync();

            if (!tiers.Any())
            {
                TempData["Error"] = "لا توجد شرائح عمولة نشطة. يرجى إضافة شرائح أولاً";
                return RedirectToAction("CommissionTiers");
            }

            // Get all pending commissions this month grouped by sales user
            var pendingCommissions = await _context.Commissions
                .Where(c => c.Status == 1
                         && c.CreatedAt.Month == month
                         && c.CreatedAt.Year == year)
                .ToListAsync();

            // Group by sales user
            var bySalesUser = pendingCommissions.GroupBy(c => c.SalesUserId);
            int updated = 0;

            foreach (var group in bySalesUser)
            {
                var salesUserId = group.Key;

                // Count approved + paid + pending for this user this month
                int totalDeals = await _context.Commissions
                    .CountAsync(c => c.SalesUserId == salesUserId
                                  && c.CreatedAt.Month == month
                                  && c.CreatedAt.Year == year
                                  && c.Status != 4); // exclude reversed

                // Find the correct tier
                var correctTier = tiers
                    .Where(t => t.MinDeals <= totalDeals
                             && (t.MaxDeals == null || t.MaxDeals >= totalDeals))
                    .OrderByDescending(t => t.MinDeals)
                    .FirstOrDefault();

                if (correctTier == null) continue;

                // Update all pending commissions for this user
                foreach (var commission in group)
                {
                    commission.AmountEgp = correctTier.AmountPerDeal;
                    commission.DealsThisMonth = totalDeals;
                    updated++;
                }
            }

            await _context.SaveChangesAsync();
            TempData["Success"] = $"تم إعادة حساب {updated} عمولة معلقة بناءً على الشرائح الحالية";
            return RedirectToAction("CommissionTiers");
        }
        // GET /Admin/Salaries
        public async Task<IActionResult> Salaries(int? month, int? year)
        {
            var now = DateTime.UtcNow;
            var selMonth = month ?? now.Month;
            var selYear = year ?? now.Year;
            var monthStart = new DateOnly(selYear, selMonth, 1);

            var adminId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            // Get all non-admin users with their salary payment for selected month
            var users = await _context.Users
                .Where(u => u.IsActive && u.Role != 1) // exclude Admin
                .OrderBy(u => u.Role)
                .ThenBy(u => u.FullName)
                .ToListAsync();

            var payments = await _context.SalaryPayments
                .Where(sp => sp.SalaryMonth == monthStart)
                .ToListAsync();

            var dto = users.Select(u => {
                var pay = payments.FirstOrDefault(p => p.UserId == u.Id);
                return new SalaryUserDto
                {
                    UserId = u.Id,
                    FullName = u.FullName,
                    Email = u.Email,
                    Role = u.Role,
                    BaseSalary = u.BaseSalary,
                    Adjustment = pay?.Adjustment ?? 0,
                    AdjustmentNote = pay?.AdjustmentNote,
                    PaymentId = pay?.Id,
                    Status = pay?.Status ?? 0
                };
            }).ToList();

            ViewBag.SelectedMonth = selMonth;
            ViewBag.SelectedYear = selYear;
            ViewBag.MonthStart = monthStart;
            ViewBag.TotalBaseSalary = dto.Sum(d => d.BaseSalary);
            ViewBag.TotalAdjustment = dto.Sum(d => d.Adjustment);
            ViewBag.TotalPayroll = dto.Sum(d => d.TotalAmount);
            ViewBag.PendingCount = dto.Count(d => d.Status == 1);
            ViewBag.ApprovedCount = dto.Count(d => d.Status == 2);
            ViewBag.PaidCount = dto.Count(d => d.Status == 3);
            ViewBag.NotCreatedCount = dto.Count(d => d.Status == 0);

            return View(dto);
        }

        // POST /Admin/SetBaseSalary
        // Update a user's base salary permanently
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetBaseSalary(Guid userId, decimal baseSalary)
        {
            if (baseSalary < 0)
            {
                TempData["Error"] = "المرتب الأساسي لا يمكن أن يكون سالباً";
                return RedirectToAction("Salaries");
            }

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound();

            user.BaseSalary = baseSalary;
            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم تحديث المرتب الأساسي لـ {user.FullName} إلى {baseSalary:N0} ج.م";
            return RedirectToAction("Salaries");
        }

        // POST /Admin/AdjustSalary
        // Add bonus or deduction for THIS month only
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdjustSalary(
            Guid userId, decimal adjustment, string? adjustmentNote,
            int month, int year)
        {
            var adminId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var monthStart = new DateOnly(year, month, 1);

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound();

            // Get or create the payment record
            var payment = await _context.SalaryPayments
                .FirstOrDefaultAsync(sp => sp.UserId == userId && sp.SalaryMonth == monthStart);

            if (payment == null)
            {
                payment = new SalaryPayment
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    SalaryMonth = monthStart,
                    BaseSalary = user.BaseSalary,
                    Adjustment = adjustment,
                    AdjustmentNote = adjustmentNote,
                    Status = 1, // Pending
                    CreatedById = adminId,
                    CreatedAt = DateTime.UtcNow
                };
                _context.SalaryPayments.Add(payment);
            }
            else
            {
                if (payment.Status == 3)
                {
                    TempData["Error"] = $"تم صرف مرتب {user.FullName} بالفعل هذا الشهر";
                    return RedirectToAction("Salaries", new { month, year });
                }
                payment.Adjustment = adjustment;
                payment.AdjustmentNote = adjustmentNote;
                payment.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            var sign = adjustment >= 0 ? "مكافأة" : "خصم";
            var absVal = Math.Abs(adjustment);
            TempData["Success"] = $"تم تسجيل {sign} {absVal:N0} ج.م لـ {user.FullName}";
            return RedirectToAction("Salaries", new { month, year });
        }

        // POST /Admin/ApproveSalary
        // Approve a single user's salary for the month
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveSalary(Guid userId, int month, int year)
        {
            var adminId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var monthStart = new DateOnly(year, month, 1);

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound();

            var payment = await _context.SalaryPayments
                .FirstOrDefaultAsync(sp => sp.UserId == userId && sp.SalaryMonth == monthStart);

            // Auto-create if doesn't exist
            if (payment == null)
            {
                payment = new SalaryPayment
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    SalaryMonth = monthStart,
                    BaseSalary = user.BaseSalary,
                    Adjustment = 0,
                    Status = 1,
                    CreatedById = adminId,
                    CreatedAt = DateTime.UtcNow
                };
                _context.SalaryPayments.Add(payment);
            }

            if (payment.Status == 3)
            {
                TempData["Error"] = $"تم صرف مرتب {user.FullName} بالفعل";
                return RedirectToAction("Salaries", new { month, year });
            }

            payment.Status = 2; // Approved
            payment.ApprovedById = adminId;
            payment.ApprovedAt = DateTime.UtcNow;
            payment.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            TempData["Success"] = $"تمت الموافقة على مرتب {user.FullName} — {payment.TotalAmount:N0} ج.م";
            return RedirectToAction("Salaries", new { month, year });
        }

        // POST /Admin/ApproveAllSalaries
        // Approve ALL users' salaries for the month at once
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveAllSalaries(int month, int year)
        {
            var adminId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var monthStart = new DateOnly(year, month, 1);

            var users = await _context.Users
                .Where(u => u.IsActive && u.Role != 1)
                .ToListAsync();

            var existingPayments = await _context.SalaryPayments
                .Where(sp => sp.SalaryMonth == monthStart)
                .ToListAsync();

            int approved = 0;

            foreach (var user in users)
            {
                var payment = existingPayments.FirstOrDefault(p => p.UserId == user.Id);

                if (payment == null)
                {
                    // Create and approve in one step
                    payment = new SalaryPayment
                    {
                        Id = Guid.NewGuid(),
                        UserId = user.Id,
                        SalaryMonth = monthStart,
                        BaseSalary = user.BaseSalary,
                        Adjustment = 0,
                        Status = 2, // Approved directly
                        ApprovedById = adminId,
                        ApprovedAt = DateTime.UtcNow,
                        CreatedById = adminId,
                        CreatedAt = DateTime.UtcNow
                    };
                    _context.SalaryPayments.Add(payment);
                    approved++;
                }
                else if (payment.Status == 1) // Only approve pending ones
                {
                    payment.Status = 2;
                    payment.ApprovedById = adminId;
                    payment.ApprovedAt = DateTime.UtcNow;
                    payment.UpdatedAt = DateTime.UtcNow;
                    approved++;
                }
                // Skip already approved (2) or paid (3)
            }

            await _context.SaveChangesAsync();

            var monthName = new DateTime(year, month, 1).ToString("MMMM yyyy");
            TempData["Success"] = $"تمت الموافقة على {approved} مرتب لشهر {monthName}";
            return RedirectToAction("Salaries", new { month, year });
        }


     
        public async Task<IActionResult> DownloadPassportsZip(Guid? packageId, Guid? id, bool newOnly = false)
        {
            var pkgId = packageId ?? id;
            if (pkgId == null) return NotFound();

            var package = await _context.JobPackages
                .FirstOrDefaultAsync(p => p.Id == pkgId.Value);
            if (package == null) return NotFound();

            // Last download for "new only" filter
            var lastDownload = await _context.PassportDownloadLogs
                .Where(l => l.PackageId == pkgId.Value)
                .OrderByDescending(l => l.DownloadedAt)
                .FirstOrDefaultAsync();

            var candidates = await _context.Candidates
                .Include(c => c.CurrentPackageStage)
                .Include(c => c.Documents.Where(d => d.DocumentType == 1))
                .Where(c => c.JobPackageId == pkgId.Value
                    && c.Documents.Any(d => d.DocumentType == 1))
                .OrderBy(c => c.FullName)
                .ToListAsync();

            // Get already-downloaded candidate IDs
            var downloadedIds = await _context.PassportDownloadedCandidates
                .Where(pdc => pdc.Log.PackageId == pkgId.Value)
                .Select(pdc => pdc.CandidateId)
                .ToListAsync();

            // Get PASSPORT_SUBMIT stage IDs
            var pkgWithStages = await _context.JobPackages
                .Include(p => p.PackageStages).ThenInclude(ps => ps.StageType)
                .FirstOrDefaultAsync(p => p.Id == pkgId.Value);
            var passportStageIds2 = pkgWithStages?.PackageStages
                .Where(ps => ps.StageType?.StageCode == "PASSPORT_SUBMIT")
                .Select(ps => ps.Id).ToList() ?? new();

            // Candidates with completed PASSPORT_SUBMIT stage action
            var completedIds = await _context.StageActionCompletions
                .Where(sc => passportStageIds2.Contains(sc.PackageStageId))
                .Select(sc => sc.CandidateId)
                .Distinct()
                .ToListAsync();

            // Only include: completed stage + moved past it + not already downloaded
            candidates = candidates.Where(c =>
                completedIds.Contains(c.Id) &&
                !downloadedIds.Contains(c.Id) &&
                !passportStageIds2.Contains(c.CurrentPackageStageId ?? Guid.Empty))
                .ToList();

            if (!candidates.Any())
            {
                TempData["Error"] = newOnly
                    ? "لا توجد جوازات جديدة منذ آخر تحميل"
                    : "لا توجد جوازات مرفوعة لهذه الباقة";
                return RedirectToAction("PackagePassports", new { packageId = pkgId });
            }

            using var memoryStream = new MemoryStream();
            int fileCount = 0;

            using (var zip = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
            {
                foreach (var candidate in candidates)
                {
                    var passportDoc = candidate.Documents
                        .Where(d => d.DocumentType == 1)
                        .OrderByDescending(d => d.UploadedAt)
                        .FirstOrDefault();

                    if (passportDoc == null) continue;

                    var filePath = Path.Combine(
                        _env.WebRootPath, "uploads", "candidates",
                        candidate.Id.ToString(),
                        Path.GetFileName(passportDoc.S3key));

                    if (!System.IO.File.Exists(filePath)) continue;

                    var ext = Path.GetExtension(passportDoc.FileName);
                    var passportNo = candidate.PassportNumber ?? "unknown";
                    var safeName = string.Concat(candidate.FullName
                                       .Split(Path.GetInvalidFileNameChars()))
                                       .Replace(" ", "_");
                    var entryName = $"{safeName}_{passportNo}{ext}";

                    var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
                    using var entryStream = entry.Open();
                    using var fileStream = System.IO.File.OpenRead(filePath);
                    await fileStream.CopyToAsync(entryStream);
                    fileCount++;
                }
            }

            // ── Log this download ────────────────────────────────────────────
            var downloadedAt = DateTime.UtcNow;
            var candidateIds = candidates.Select(c => c.Id).ToList();

            // Log this download
            var logEntry = new PassportDownloadLog
            {
                Id = Guid.NewGuid(),
                PackageId = pkgId.Value,
                DownloadedById = CurrentUserId,
                DownloadedAt = downloadedAt,
                PassportCount = fileCount,
                IsNewOnly = newOnly
            };
            _context.PassportDownloadLogs.Add(logEntry);
            await _context.SaveChangesAsync();

            // Track each candidate — won't appear in future downloads
            foreach (var candId in candidateIds)
            {
                var alreadyTracked = await _context.PassportDownloadedCandidates
                    .AnyAsync(pdc => pdc.CandidateId == candId);
                if (!alreadyTracked)
                {
                    _context.PassportDownloadedCandidates.Add(new PassportDownloadedCandidate
                    {
                        Id = Guid.NewGuid(),
                        LogId = logEntry.Id,
                        CandidateId = candId,
                        DownloadedAt = downloadedAt
                    });
                }
            }
            await _context.SaveChangesAsync();

            // ── Auto-move candidates on PASSPORT_SEND stage ──────────────────
            // Only moves candidates whose passport was actually in this ZIP
            var passportSendType = await _context.StageTypes
                .FirstOrDefaultAsync(st => st.StageCode == "PASSPORT_SEND");

            if (passportSendType != null)
            {
                var onPassportSend = await _context.Candidates
                    .Include(c => c.CurrentPackageStage)
                    .Where(c => candidateIds.Contains(c.Id)
                        && c.CurrentPackageStage != null
                        && c.CurrentPackageStage.StageTypeId == passportSendType.Id)
                    .ToListAsync();

                foreach (var cand in onPassportSend)
                {
                    var spSuccess = new SqlParameter { ParameterName = "@Success", SqlDbType = System.Data.SqlDbType.Bit, Direction = System.Data.ParameterDirection.Output };
                    var spMessage = new SqlParameter { ParameterName = "@Message", SqlDbType = System.Data.SqlDbType.NVarChar, Size = 500, Direction = System.Data.ParameterDirection.Output };
                    var spStage = new SqlParameter { ParameterName = "@NewStageName", SqlDbType = System.Data.SqlDbType.NVarChar, Size = 200, Direction = System.Data.ParameterDirection.Output };

                    await _context.Database.ExecuteSqlRawAsync(
                        "EXEC demorecruitment.sp_MoveToNextStage @CandidateId, @MovedById, @Notes, @IsOverride, @OverrideReason, @Success OUTPUT, @Message OUTPUT, @NewStageName OUTPUT",
                        new SqlParameter("@CandidateId", cand.Id),
                        new SqlParameter("@MovedById", CurrentUserId),
                        new SqlParameter("@Notes", (object)"تم إرسال الجواز للوكيل — تحميل تلقائي"),
                        new SqlParameter("@IsOverride", false),
                        new SqlParameter("@OverrideReason", DBNull.Value),
                        spSuccess, spMessage, spStage
                    );
                }
            }

            memoryStream.Seek(0, SeekOrigin.Begin);

            var safePkgName = string.Concat(package.Name
                                .Split(Path.GetInvalidFileNameChars()))
                                .Replace(" ", "_");
            var suffix = newOnly ? "_جديد" : "_كل";
            var zipName = $"{safePkgName}{suffix}_{DateTime.Now:yyyyMMdd_HHmm}.zip";

            return File(memoryStream.ToArray(), "application/zip", zipName);
        }
    }

    public class CommissionTierDto
    {
        public Guid Id { get; set; }
        public int MinDeals { get; set; }
        public int? MaxDeals { get; set; }
        public decimal AmountPerDeal { get; set; }
        public bool IsActive { get; set; }
        public string RangeLabel => MaxDeals.HasValue
            ? $"{MinDeals} - {MaxDeals} صفقة"
            : $"{MinDeals}+ صفقة";
    }
    public class StageOrderDto
    {
        public Guid StageId { get; set; }
        public int Order { get; set; }
    }
}