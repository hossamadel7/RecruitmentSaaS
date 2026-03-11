using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using System.Security.Claims;

namespace RecruitmentSaaS.Controllers
{
    [Authorize(Roles = "1")]
    public class AdminController : Controller
    {
        private readonly RecruitmentCrmContext _context;

        public AdminController(RecruitmentCrmContext context)
        {
            _context = context;
        }

        private Guid CurrentUserId =>
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // ── GET /Admin/Index ────────────────────────────────────────────────
        public async Task<IActionResult> Index()
        {
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

            // Leads by status
            var leadsByStatus = await _context.Leads
                .GroupBy(l => l.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .OrderBy(g => g.Status)
                .ToListAsync();

            // Candidates by stage
            var candidatesByStage = await _context.Candidates
                .Where(c => c.IsCompleted != true)
                .GroupBy(c => c.CurrentStage)
                .Select(g => new { Stage = g.Key, Count = g.Count() })
                .OrderBy(g => g.Stage)
                .ToListAsync();

            // Recent leads (last 5)
            var recentLeads = await _context.Leads
                .Include(l => l.Campaign)
                .Include(l => l.AssignedSales)
                .OrderByDescending(l => l.CreatedAt)
                .Take(5)
                .ToListAsync();

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
                BranchId = branchId ?? Guid.Empty,
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
        public async Task<IActionResult> Candidates(string? q, int? stage, int page = 1)
        {
            const int pageSize = 25;

            var query = _context.Candidates
                .Include(c => c.JobPackage)
                .Include(c => c.AssignedSales)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(c =>
                    c.FullName.Contains(q) ||
                    c.Phone.Contains(q) ||
                    (c.NationalId != null && c.NationalId.Contains(q)));

            if (stage.HasValue)
                query = query.Where(c => c.CurrentStage == stage.Value);

            query = query.OrderByDescending(c => c.CreatedAt);

            var total = await query.CountAsync();
            var candidates = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.Q = q;
            ViewBag.Stage = stage;
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

            // Default: show pending (status=1)
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

            return View(commissions);
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
    }
}
