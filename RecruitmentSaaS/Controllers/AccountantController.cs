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
            var pendingCount = await _context.Payments
                .CountAsync(p => p.Status == 1);

            var approvedToday = await _context.Payments
                .Where(p => p.Status == 2 && p.ApprovedAt != null
                    && p.ApprovedAt.Value.Date == DateTime.UtcNow.Date)
                .SumAsync(p => (decimal?)p.AmountEgp) ?? 0;

            var rejectedCount = await _context.Payments
                .CountAsync(p => p.Status == 3);

            var totalApproved = await _context.Payments
                .Where(p => p.Status == 2)
                .SumAsync(p => (decimal?)p.AmountEgp) ?? 0;

            // Recent pending payments
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

            ViewBag.PendingCount   = pendingCount;
            ViewBag.ApprovedToday  = approvedToday;
            ViewBag.RejectedCount  = rejectedCount;
            ViewBag.TotalApproved  = totalApproved;
            ViewBag.RecentPending  = recentPending;

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

            ViewBag.Page      = page;
            ViewBag.PageSize  = pageSize;
            ViewBag.Total     = total;
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
                .FirstOrDefaultAsync(p => p.Id == paymentId && p.Status == 1);

            if (payment == null)
            {
                TempData["Error"] = "الدفعة غير موجودة أو تمت معالجتها مسبقاً";
                return RedirectToAction("Pending");
            }

            payment.Status      = 2; // Approved
            payment.ApprovedById = userId;
            payment.ApprovedAt  = DateTime.UtcNow;
            if (!string.IsNullOrEmpty(notes))
                payment.Notes = (payment.Notes ?? "") + $" | ملاحظة المحاسب: {notes}";

            // Now update TotalPaidEgp on candidate
            var candidate = payment.Candidate;
            if (payment.TransactionType == 1)
                candidate.TotalPaidEgp += payment.AmountEgp;
            else if (payment.TransactionType == 2)
                candidate.TotalPaidEgp -= payment.AmountEgp;

            candidate.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

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

            payment.Status          = 3; // Rejected
            payment.ApprovedById    = userId;
            payment.ApprovedAt      = DateTime.UtcNow;
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
                query = query.Where(p => p.Status != 1); // exclude pending from history

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

            ViewBag.Page       = page;
            ViewBag.PageSize   = pageSize;
            ViewBag.Total      = total;
            ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
            ViewBag.Status     = status;

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

            var totalApproved = payments
                .Where(p => p.Status == 2)
                .Sum(p => p.TransactionType == 2 ? -p.AmountEGP : p.AmountEGP);

            var totalPending = payments
                .Where(p => p.Status == 1)
                .Sum(p => p.AmountEGP);

            ViewBag.TotalApproved = totalApproved;
            ViewBag.TotalPending  = totalPending;
            ViewBag.Candidate     = candidate;

            return View(payments);
        }
    }
}
