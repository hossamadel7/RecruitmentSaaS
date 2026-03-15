using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Services
{
    public enum NotificationType : byte
    {
        ApprovalRequest = 1,
        StageMove       = 2,
        Payment         = 3,
        Refund          = 4,
        Commission      = 5,
        General         = 6
    }

    public interface INotificationService
    {
        // Send to specific user
        Task SendAsync(Guid userId, string title, string message,
                       string? link = null, NotificationType type = NotificationType.General);

        // Send to all users with a specific role
        Task SendToRoleAsync(int role, string title, string message,
                             string? link = null, NotificationType type = NotificationType.General);

        // Send to all admins (Role = 1)
        Task SendToAdminsAsync(string title, string message,
                               string? link = null, NotificationType type = NotificationType.General);

        // Get unread count for a user
        Task<int> GetUnreadCountAsync(Guid userId);

        // Get recent notifications for a user
        Task<List<Notification>> GetRecentAsync(Guid userId, int take = 10);

        // Mark as read
        Task MarkAsReadAsync(Guid notificationId, Guid userId);

        // Mark all as read
        Task MarkAllAsReadAsync(Guid userId);
    }

    public class NotificationService : INotificationService
    {
        private readonly RecruitmentCrmContext _context;

        public NotificationService(RecruitmentCrmContext context)
        {
            _context = context;
        }

        // ── Send to specific user ────────────────────────────────────────────
        public async Task SendAsync(Guid userId, string title, string message,
                                    string? link = null, NotificationType type = NotificationType.General)
        {
            _context.Notifications.Add(new Notification
            {
                Id        = Guid.NewGuid(),
                UserId    = userId,
                Title     = title,
                Body      = message,
                Link      = link,
                Type      = (byte)type,
                IsRead    = false,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }

        // ── Send to all users with a specific role ───────────────────────────
        public async Task SendToRoleAsync(int role, string title, string message,
                                          string? link = null, NotificationType type = NotificationType.General)
        {
            var users = await _context.Users
                .Where(u => u.Role == role && u.IsActive == true)
                .Select(u => u.Id)
                .ToListAsync();

            if (!users.Any()) return;

            var now = DateTime.UtcNow;
            foreach (var userId in users)
            {
                _context.Notifications.Add(new Notification
                {
                    Id        = Guid.NewGuid(),
                    UserId    = userId,
                    Title     = title,
                    Body      = message,
                    Link      = link,
                    Type      = (byte)type,
                    IsRead    = false,
                    CreatedAt = now
                });
            }
            await _context.SaveChangesAsync();
        }

        // ── Send to all admins ───────────────────────────────────────────────
        public async Task SendToAdminsAsync(string title, string message,
                                            string? link = null, NotificationType type = NotificationType.General)
        {
            await SendToRoleAsync(1, title, message, link, type);
        }

        // ── Get unread count ─────────────────────────────────────────────────
        public async Task<int> GetUnreadCountAsync(Guid userId)
        {
            return await _context.Notifications
                .CountAsync(n => n.UserId == userId && n.IsRead == false);
        }

        // ── Get recent notifications ─────────────────────────────────────────
        public async Task<List<Notification>> GetRecentAsync(Guid userId, int take = 10)
        {
            return await _context.Notifications
                .Where(n => n.UserId == userId)
                .OrderByDescending(n => n.CreatedAt)
                .Take(take)
                .ToListAsync();
        }

        // ── Mark single notification as read ─────────────────────────────────
        public async Task MarkAsReadAsync(Guid notificationId, Guid userId)
        {
            var notif = await _context.Notifications
                .FirstOrDefaultAsync(n => n.Id == notificationId && n.UserId == userId);

            if (notif != null && notif.IsRead == false)
            {
                notif.IsRead = true;
                await _context.SaveChangesAsync();
            }
        }

        // ── Mark all as read ─────────────────────────────────────────────────
        public async Task MarkAllAsReadAsync(Guid userId)
        {
            var unread = await _context.Notifications
                .Where(n => n.UserId == userId && n.IsRead == false)
                .ToListAsync();

            if (!unread.Any()) return;

            var now = DateTime.UtcNow;
            foreach (var n in unread)
            {
                n.IsRead = true;
            }
            await _context.SaveChangesAsync();
        }
    }
}
