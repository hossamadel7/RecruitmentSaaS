using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Services;
using System.Security.Claims;

namespace RecruitmentSaaS.Controllers
{
    [Authorize]
    public class NotificationsController : Controller
    {
        private readonly RecruitmentCrmContext _context;
        private readonly INotificationService  _notificationService;

        public NotificationsController(RecruitmentCrmContext context, INotificationService notificationService)
        {
            _context             = context;
            _notificationService = notificationService;
        }

        private Guid CurrentUserId =>
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // ── GET /Notifications ───────────────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            var notifications = await _context.Notifications
                .Where(n => n.UserId == CurrentUserId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(50)
                .ToListAsync();

            // Mark all as read when user opens the page
            await _notificationService.MarkAllAsReadAsync(CurrentUserId);

            return View(notifications);
        }

        // ── GET /Notifications/UnreadCount (AJAX) ────────────────────────────
        [HttpGet]
        public async Task<IActionResult> UnreadCount()
        {
            var count = await _notificationService.GetUnreadCountAsync(CurrentUserId);
            return Json(new { count });
        }

        // ── GET /Notifications/Recent (AJAX dropdown) ────────────────────────
        [HttpGet]
        public async Task<IActionResult> Recent()
        {
            var notifications = await _notificationService.GetRecentAsync(CurrentUserId, 8);
            var unreadCount   = notifications.Count(n => n.IsRead == false);

            var result = notifications.Select(n => new
            {
                n.Id,
                n.Title,
                Message = n.Body,
                n.Link,
                n.Type,
                n.IsRead,
                n.CreatedAt,
                TimeAgo = GetTimeAgo(n.CreatedAt)
            });

            return Json(new { notifications = result, unreadCount });
        }

        // ── POST /Notifications/MarkRead ─────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> MarkRead(Guid id)
        {
            await _notificationService.MarkAsReadAsync(id, CurrentUserId);
            return Json(new { success = true });
        }

        // ── POST /Notifications/MarkAllRead ──────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> MarkAllRead()
        {
            await _notificationService.MarkAllAsReadAsync(CurrentUserId);
            return Json(new { success = true });
        }

        // ── Helper ───────────────────────────────────────────────────────────
        private static string GetTimeAgo(DateTime dt)
        {
            var diff = DateTime.UtcNow - dt;
            if (diff.TotalMinutes < 1)  return "الآن";
            if (diff.TotalMinutes < 60) return $"منذ {(int)diff.TotalMinutes} دقيقة";
            if (diff.TotalHours   < 24) return $"منذ {(int)diff.TotalHours} ساعة";
            if (diff.TotalDays    < 7)  return $"منذ {(int)diff.TotalDays} يوم";
            return dt.ToString("dd/MM/yyyy");
        }
    }
}
