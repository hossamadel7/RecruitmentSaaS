using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.DTOs;
using RecruitmentSaaS.Models.Entities;
using System.Security.Claims;

namespace RecruitmentSaaS.Controllers
{
    [Authorize(Roles = "4")]
    public class AccountantController : Controller
    {
        private readonly RecruitmentCrmContext _context;

        public AccountantController(RecruitmentCrmContext context)
        {
            _context = context;
        }

        private Guid CurrentUserId =>
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private string CurrentUserName =>
            User.FindFirstValue(ClaimTypes.Name) ?? "محاسب";

        // ── GET /Accountant/Index ─────────────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            var pendingPayments = await _context.Payments.CountAsync(p => p.Status == 1);
            var pendingRefunds = await _context.Refunds.CountAsync(r => r.Status == 2);
            var pendingCommissions = await _context.Commissions.CountAsync(c => c.Status == 2);

            var approvedToday = await _context.Payments
                .Where(p => p.Status == 2 && p.ApprovedAt != null
                    && p.ApprovedAt.Value.Date == DateTime.UtcNow.Date)
                .SumAsync(p => (decimal?)p.AmountEgp) ?? 0;

            var totalApproved = await _context.Payments
                .Where(p => p.Status == 2)
                .SumAsync(p => (decimal?)p.AmountEgp) ?? 0;

            var recentPending = await _context.Payments
                .Include(p => p.RecordedBy)
                .Include(p => p.Candidate)
                .Where(p => p.Status == 1)
                .OrderBy(p => p.CreatedAt)
                .Take(5)
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
                    CreatedAt = p.CreatedAt
                })
                .ToListAsync();

            ViewBag.PendingPayments = pendingPayments;
            ViewBag.PendingRefunds = pendingRefunds;
            ViewBag.PendingCommissions = pendingCommissions;
            ViewBag.ApprovedToday = approvedToday;
            ViewBag.TotalApproved = totalApproved;
            ViewBag.RecentPending = recentPending;

            return View();
        }

        // ── GET /Accountant/Pending ───────────────────────────────────────────
        public async Task<IActionResult> Pending(int page = 1)
        {
            const int pageSize = 20;

            var query = _context.Payments
                .Include(p => p.RecordedBy)
                .Include(p => p.Candidate)
                    .ThenInclude(c => c.JobPackage)
                .Where(p => p.Status == 1)
                .OrderBy(p => p.CreatedAt);

            var total = await query.CountAsync();

            var payments = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new AccountantPaymentDto
                {
                    Id = p.Id,
                    CandidateId = p.CandidateId,
                    CandidateFullName = p.Candidate.FullName,
                    CandidatePhone = p.Candidate.Phone,
                    JobPackageName = p.Candidate.JobPackage.Name,
                    AmountEGP = p.AmountEgp,
                    PaymentDate = p.PaymentDate,
                    PaymentMethod = p.PaymentMethod,
                    TransactionType = p.TransactionType,
                    Status = p.Status,
                    Notes = p.Notes,
                    RecordedByName = p.RecordedBy.FullName,
                    CreatedAt = p.CreatedAt
                })
                .ToListAsync();

            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.Total = total;
            ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);

            return View(payments);
        }

        // ── POST /Accountant/ApprovePayment ──────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApprovePayment(Guid paymentId, string? notes)
        {
            var userId = CurrentUserId;

            var payment = await _context.Payments
                .Include(p => p.Candidate)
                    .ThenInclude(c => c.JobPackage)
                .FirstOrDefaultAsync(p => p.Id == paymentId && p.Status == 1);

            if (payment == null)
            {
                TempData["Error"] = "الدفعة غير موجودة أو تمت معالجتها مسبقاً";
                return RedirectToAction("Pending");
            }

            // ── اعتماد الدفعة ──────────────────────────────────────────────
            payment.Status = 2;
            payment.ApprovedById = userId;
            payment.ApprovedAt = DateTime.UtcNow;

            if (!string.IsNullOrEmpty(notes))
                payment.Notes = (payment.Notes ?? "") + $" | ملاحظة المحاسب: {notes}";

            var candidate = payment.Candidate;

            if (payment.TransactionType == 1)
                candidate.TotalPaidEgp += payment.AmountEgp;
            else if (payment.TransactionType == 2)
                candidate.TotalPaidEgp -= payment.AmountEgp;

            candidate.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // ── إنشاء Commission تلقائياً عند اكتمال سعر الباقة ─────────────
            if (payment.TransactionType == 1
                && candidate.AssignedSalesId != Guid.Empty
                && candidate.TotalPaidEgp >= candidate.JobPackage.PriceEgp)
            {
                // تحقق إن مفيش Commission موجودة بالفعل لهذا المرشح
                var alreadyExists = await _context.Commissions
                    .AnyAsync(c => c.CandidateId == candidate.Id
                                && c.Status != 4);

                if (!alreadyExists)
                {
                    var settings = await _context.CommissionSettings.FirstOrDefaultAsync();
                    byte resetDay = settings?.ResetDayOfMonth ?? 1;
                    var nowUtc = DateTime.UtcNow;

                    DateOnly periodStart;
                    if (nowUtc.Day >= resetDay)
                        periodStart = new DateOnly(nowUtc.Year, nowUtc.Month, resetDay);
                    else
                    {
                        var prev = nowUtc.AddMonths(-1);
                        var safeDay = Math.Min(resetDay, DateTime.DaysInMonth(prev.Year, prev.Month));
                        periodStart = new DateOnly(prev.Year, prev.Month, safeDay);
                    }

                    int dealsThisPeriod = await _context.Commissions
                        .CountAsync(c => c.SalesUserId == candidate.AssignedSalesId
                                      && c.CommissionMonth == periodStart
                                      && c.Status != 4) + 1;

                    var tier = await _context.CommissionTiers
                        .Where(t => t.IsActive
                                 && t.MinDeals <= dealsThisPeriod
                                 && (t.MaxDeals == null || t.MaxDeals >= dealsThisPeriod))
                        .OrderByDescending(t => t.MinDeals)
                        .FirstOrDefaultAsync();

                    _context.Commissions.Add(new Commission
                    {
                        Id = Guid.NewGuid(),
                        SalesUserId = candidate.AssignedSalesId,
                        CandidateId = candidate.Id,
                        CommissionMonth = periodStart,
                        AmountEgp = tier?.AmountPerDeal ?? 0,
                        DealsThisMonth = dealsThisPeriod,
                        Status = 1,
                        CreatedAt = nowUtc
                    });

                    await _context.SaveChangesAsync();
                }
            }

            TempData["Success"] = $"تم اعتماد الدفعة بمبلغ {payment.AmountEgp:N0} ج.م";
            return RedirectToAction("Pending");
        }


        // ── POST /Accountant/RejectPayment ───────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectPayment(Guid paymentId, string rejectionReason)
        {
            var userId = CurrentUserId;

            var payment = await _context.Payments
                .FirstOrDefaultAsync(p => p.Id == paymentId && p.Status == 1);

            if (payment == null)
            {
                TempData["Error"] = "الدفعة غير موجودة أو تمت معالجتها مسبقاً";
                return RedirectToAction("Pending");
            }

            payment.Status = 3;
            payment.ApprovedById = userId;
            payment.ApprovedAt = DateTime.UtcNow;
            payment.RejectionReason = rejectionReason;

            await _context.SaveChangesAsync();

            TempData["Error"] = $"تم رفض الدفعة بمبلغ {payment.AmountEgp:N0} ج.م";
            return RedirectToAction("Pending");
        }

        // ── GET /Accountant/History ───────────────────────────────────────────
        public async Task<IActionResult> History(byte? status, int page = 1)
        {
            const int pageSize = 20;

            var query = _context.Payments
                .Include(p => p.RecordedBy)
                .Include(p => p.ApprovedBy)
                .Include(p => p.Candidate)
                    .ThenInclude(c => c.JobPackage)
                .AsQueryable();

            if (status.HasValue)
                query = query.Where(p => p.Status == status.Value);
            else
                query = query.Where(p => p.Status != 1);

            query = query.OrderByDescending(p => p.ApprovedAt ?? p.CreatedAt);

            var total = await query.CountAsync();

            var payments = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new AccountantPaymentDto
                {
                    Id = p.Id,
                    CandidateId = p.CandidateId,
                    CandidateFullName = p.Candidate.FullName,
                    CandidatePhone = p.Candidate.Phone,
                    JobPackageName = p.Candidate.JobPackage.Name,
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

            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.Total = total;
            ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
            ViewBag.Status = status;

            return View(payments);
        }

        // ── GET /Accountant/CandidateFinancials/{id} ──────────────────────────
        public async Task<IActionResult> CandidateFinancials(Guid id)
        {
            var candidate = await _context.Candidates
                .Include(c => c.JobPackage)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (candidate == null) return NotFound();

            var payments = await _context.Payments
                .Include(p => p.RecordedBy)
                .Include(p => p.ApprovedBy)
                .Where(p => p.CandidateId == id)
                .OrderByDescending(p => p.CreatedAt)
                .Select(p => new AccountantPaymentDto
                {
                    Id = p.Id,
                    CandidateId = p.CandidateId,
                    CandidateFullName = candidate.FullName,
                    CandidatePhone = candidate.Phone,
                    JobPackageName = candidate.JobPackage.Name,
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

            var refunds = await _context.Refunds
                .Include(r => r.RequestedBy)
                .Include(r => r.ReviewedBy)
                .Where(r => r.CandidateId == id)
                .OrderByDescending(r => r.RequestedAt)
                .ToListAsync();

            var totalApproved = payments.Where(p => p.Status == 2 && p.TransactionType == 1).Sum(p => p.AmountEGP);
            var totalRefunded = refunds.Where(r => r.Status == 4).Sum(r => r.AmountEgp);
            var totalPending = payments.Where(p => p.Status == 1).Sum(p => p.AmountEGP);

            ViewBag.TotalApproved = totalApproved;
            ViewBag.TotalRefunded = totalRefunded;
            ViewBag.TotalPending = totalPending;
            ViewBag.Candidate = candidate;
            ViewBag.Refunds = refunds;

            return View(payments);
        }

        // ── GET /Accountant/Payments ──────────────────────────────────────────
        public async Task<IActionResult> Payments(string? q, byte? status, int page = 1)
        {
            const int pageSize = 25;

            var query = _context.Payments
                .Include(p => p.RecordedBy)
                .Include(p => p.ApprovedBy)
                .Include(p => p.Candidate)
                    .ThenInclude(c => c.JobPackage)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(p =>
                    p.Candidate.FullName.Contains(q) ||
                    p.Candidate.Phone.Contains(q) ||
                    (p.Candidate.NationalId != null && p.Candidate.NationalId.Contains(q)));

            if (status.HasValue)
                query = query.Where(p => p.Status == status.Value);

            query = query.OrderByDescending(p => p.CreatedAt);

            var total = await query.CountAsync();

            var payments = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new AccountantPaymentDto
                {
                    Id = p.Id,
                    CandidateId = p.CandidateId,
                    CandidateFullName = p.Candidate.FullName,
                    CandidatePhone = p.Candidate.Phone,
                    JobPackageName = p.Candidate.JobPackage.Name,
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

            ViewBag.Q = q;
            ViewBag.StatusFilter = status;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.Total = total;
            ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);

            return View(payments);
        }

        // ── GET /Accountant/Salaries ──────────────────────────────────────────
        public async Task<IActionResult> Salaries(int? month, int? year)
        {
            var now = DateTime.UtcNow;
            var selMonth = month ?? now.Month;
            var selYear = year ?? now.Year;
            var monthStart = new DateOnly(selYear, selMonth, 1);

            var payments = await _context.SalaryPayments
                .Where(sp => sp.SalaryMonth == monthStart && sp.Status == 2)
                .Include(sp => sp.User)
                .OrderBy(sp => sp.User.Role)
                .ThenBy(sp => sp.User.FullName)
                .Select(sp => new SalaryUserDto
                {
                    UserId = sp.UserId,
                    FullName = sp.User.FullName,
                    Email = sp.User.Email,
                    Role = sp.User.Role,
                    BaseSalary = sp.BaseSalary,
                    Adjustment = sp.Adjustment,
                    AdjustmentNote = sp.AdjustmentNote,
                    PaymentId = sp.Id,
                    Status = sp.Status
                })
                .ToListAsync();

            ViewBag.SelectedMonth = selMonth;
            ViewBag.SelectedYear = selYear;
            ViewBag.TotalPayroll = payments.Sum(p => p.TotalAmount);

            return View(payments);
        }

        // ── POST /Accountant/FulfillSalary ────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FulfillSalary(Guid paymentId, int month, int year)
        {
            var accountantId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var payment = await _context.SalaryPayments
                .Include(sp => sp.User)
                .FirstOrDefaultAsync(sp => sp.Id == paymentId);

            if (payment == null) return NotFound();

            if (payment.Status != 2)
            {
                TempData["Error"] = "هذا المرتب غير معتمد أو تم صرفه بالفعل";
                return RedirectToAction("Salaries", new { month, year });
            }

            payment.Status = 3;
            payment.PaidById = accountantId;
            payment.PaidAt = DateTime.UtcNow;
            payment.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            TempData["Success"] = $"تم صرف مرتب {payment.User.FullName} — {payment.TotalAmount:N0} ج.م";
            return RedirectToAction("Salaries", new { month, year });
        }

        // ── GET /Accountant/AllCandidates ─────────────────────────────────────
        public async Task<IActionResult> AllCandidates(string? q, int page = 1)
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

            query = query.OrderByDescending(c => c.CreatedAt);

            var total = await query.CountAsync();

            var candidates = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new
                {
                    c.Id,
                    c.FullName,
                    c.Phone,
                    c.NationalId,
                    CurrentStageName = c.CurrentPackageStage != null ? c.CurrentPackageStage.StageName : "—",
                    c.TotalPaidEgp,
                    PriceEgp = c.JobPackage.PriceEgp,
                    JobPackageName = c.JobPackage.Name,
                    AssignedSalesName = c.AssignedSales != null ? c.AssignedSales.FullName : null,
                    c.IsCompleted,
                    c.CreatedAt,
                    PendingAmount = _context.Payments
                        .Where(p => p.CandidateId == c.Id && p.Status == 1)
                        .Sum(p => (decimal?)p.AmountEgp) ?? 0
                })
                .ToListAsync();

            ViewBag.Q = q;
            ViewBag.Page = page;
            ViewBag.PageSize = pageSize;
            ViewBag.Total = total;
            ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);

            return View(candidates);
        }

        // ── GET /Accountant/Refunds ───────────────────────────────────────────
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

            if (status.HasValue)
                query = query.Where(r => r.Status == status.Value);
            else
                query = query.Where(r => r.Status == 2);

            query = query.OrderBy(r => r.ReviewedAt);

            var total = await query.CountAsync();

            var refunds = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.StatusFilter = status ?? (byte)2;
            ViewBag.Page = page;
            ViewBag.Total = total;
            ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);

            return View(refunds);
        }

        // ── POST /Accountant/ExecuteRefund ────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExecuteRefund(Guid refundId)
        {
            var userId = CurrentUserId;

            var refund = await _context.Refunds
                .Include(r => r.Candidate)
                .FirstOrDefaultAsync(r => r.Id == refundId && r.Status == 2);

            if (refund == null)
            {
                TempData["Error"] = "طلب الاسترداد غير موجود أو لم يتم اعتماده من الإدارة بعد";
                return RedirectToAction("Refunds");
            }

            refund.Candidate.TotalPaidEgp =
                Math.Max(0, refund.Candidate.TotalPaidEgp - refund.AmountEgp);
            refund.Candidate.UpdatedAt = DateTime.UtcNow;

            refund.Status = 4;
            refund.ExecutedById = userId;
            refund.ExecutedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم تنفيذ الاسترداد بمبلغ {refund.AmountEgp:N0} ج.م";
            return RedirectToAction("Refunds");
        }

        // ── GET /Accountant/Commissions ───────────────────────────────────────
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
                query = query.Where(c => c.Status == 2);

            query = query.OrderBy(c => c.ApprovedAt);

            var total = await query.CountAsync();

            var commissions = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.StatusFilter = status ?? (byte)2;
            ViewBag.Page = page;
            ViewBag.Total = total;
            ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);

            return View(commissions);
        }

        // ── POST /Accountant/PayCommission ────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PayCommission(Guid commissionId)
        {
            var userId = CurrentUserId;

            var commission = await _context.Commissions
                .FirstOrDefaultAsync(c => c.Id == commissionId && c.Status == 2);

            if (commission == null)
            {
                TempData["Error"] = "العمولة غير موجودة أو لم يتم اعتمادها من الإدارة";
                return RedirectToAction("Commissions");
            }

            commission.Status = 3;
            commission.PaidById = userId;
            commission.PaidAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            TempData["Success"] = $"تم تسجيل صرف العمولة بمبلغ {commission.AmountEgp:N0} ج.م";
            return RedirectToAction("Commissions");
        }
        // POST /Accountant/PayAllUserCommissions
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PayAllUserCommissions(Guid salesUserId)
        {
            var userId = CurrentUserId;

            var commissions = await _context.Commissions
                .Where(c => c.SalesUserId == salesUserId && c.Status == 2)
                .ToListAsync();

            if (!commissions.Any())
            {
                TempData["Error"] = "لا توجد عمولات معتمدة لهذا الموظف";
                return RedirectToAction("Commissions");
            }

            foreach (var c in commissions)
            {
                c.Status = 3;
                c.PaidById = userId;
                c.PaidAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            var user = await _context.Users.FindAsync(salesUserId);
            TempData["Success"] = $"تم صرف عمولات {user?.FullName} بنجاح";
            return RedirectToAction("Commissions");
        }
        // ── GET /Accountant/Reports ───────────────────────────────────────────
        public async Task<IActionResult> Reports(int? month, int? year)
        {
            var now = DateTime.UtcNow;
            month ??= now.Month;
            year ??= now.Year;

            var from = new DateTime(year.Value, month.Value, 1);
            var to = from.AddMonths(1);
            var monthStart = new DateOnly(year.Value, month.Value, 1);

            var paymentsThisMonth = await _context.Payments
                .Where(p => p.Status == 2 && p.ApprovedAt >= from && p.ApprovedAt < to && p.TransactionType == 1)
                .GroupBy(p => p.PaymentMethod)
                .Select(g => new { Method = g.Key, Total = g.Sum(p => p.AmountEgp), Count = g.Count() })
                .ToListAsync();

            var refundsThisMonth = await _context.Refunds
                .Where(r => r.Status == 4 && r.ExecutedAt >= from && r.ExecutedAt < to)
                .SumAsync(r => (decimal?)r.AmountEgp) ?? 0;

            var commissionsThisMonth = await _context.Commissions
                .Where(c => c.Status == 3 && c.PaidAt >= from && c.PaidAt < to)
                .SumAsync(c => (decimal?)c.AmountEgp) ?? 0;

            // ── المرتبات المصروفة هذا الشهر ──────────────────────────────────
            var salariesThisMonth = await _context.SalaryPayments
                .Where(sp => sp.Status == 3 && sp.SalaryMonth == monthStart)
                .SumAsync(sp => (decimal?)(sp.BaseSalary + sp.Adjustment)) ?? 0;

            // تفاصيل المرتبات للعرض في الجدول
            var salaryDetails = await _context.SalaryPayments
                .Include(sp => sp.User)
                .Where(sp => sp.Status == 3 && sp.SalaryMonth == monthStart)
                .OrderBy(sp => sp.User.Role)
                .ThenBy(sp => sp.User.FullName)
                .Select(sp => new {
                    FullName = sp.User.FullName,
                    Role = sp.User.Role,
                    BaseSalary = sp.BaseSalary,
                    Adjustment = sp.Adjustment,
                    Total = sp.BaseSalary + sp.Adjustment
                })
                .ToListAsync();

            var candidatesThisMonth = await _context.Candidates
                .CountAsync(c => c.CreatedAt >= from && c.CreatedAt < to);

            var completedThisMonth = await _context.Candidates
                .CountAsync(c => c.IsCompleted == true && c.CompletedAt >= from && c.CompletedAt < to);

            var bySales = await _context.Payments
                .Include(p => p.Candidate)
                    .ThenInclude(c => c.AssignedSales)
                .Where(p => p.Status == 2 && p.ApprovedAt >= from && p.ApprovedAt < to && p.TransactionType == 1)
                .GroupBy(p => new { p.Candidate.AssignedSalesId, Name = p.Candidate.AssignedSales!.FullName })
                .Select(g => new { SalesName = g.Key.Name, Total = g.Sum(p => p.AmountEgp), Count = g.Count() })
                .OrderByDescending(g => g.Total)
                .ToListAsync();

            ViewBag.Month = month;
            ViewBag.Year = year;
            ViewBag.PaymentsThisMonth = paymentsThisMonth;
            ViewBag.RefundsThisMonth = refundsThisMonth;
            ViewBag.CommissionsThisMonth = commissionsThisMonth;
            ViewBag.SalariesThisMonth = salariesThisMonth;
            ViewBag.SalaryDetails = salaryDetails;
            ViewBag.CandidatesThisMonth = candidatesThisMonth;
            ViewBag.CompletedThisMonth = completedThisMonth;
            ViewBag.BySales = bySales;
            ViewBag.TotalCollected = paymentsThisMonth.Sum(p => (decimal)p.Total);

            return View();
        }
    } // ← إغلاق AccountantController ✅

    // ✅ SalaryUserDto خارج الـ class — هنا صح
    public class SalaryUserDto
    {
        public DateTime? PaidAt { get; set; }
        public string? PaidAgoText { get; set; }
        
        public Guid UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public byte Role { get; set; }
        public string RoleName => Role switch
        {
            2 => "استقبال",
            3 => "تيلي سيلز",
            4 => "محاسب",
            5 => "عمليات",
            6 => "مبيعات مكتبية",
            _ => "موظف"
        };
        public decimal BaseSalary { get; set; }
        public decimal Adjustment { get; set; }
        public string? AdjustmentNote { get; set; }
        public decimal TotalAmount => BaseSalary + Adjustment;
        public Guid? PaymentId { get; set; }
        public byte Status { get; set; }
        public string StatusLabel => Status switch
        {
            1 => "معلق",
            2 => "معتمد",
            3 => "مدفوع",
            _ => "لم يُعدّ بعد"
        };
        public bool IsApproved => Status == 2;
        public bool IsPaid => Status == 3;
    }

} // ← إغلاق namespace ✅