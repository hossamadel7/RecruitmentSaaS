using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;

namespace RecruitmentSaaS.Services
{
    /// <summary>
    /// Runs daily at 9 AM — sends notifications to TeleSales for:
    /// 1. Appointments scheduled for tomorrow (Status 5)
    /// 2. Due follow-up reminders today
    /// </summary>
    public class AppointmentReminderService : BackgroundService
    {
        private readonly IServiceProvider _services;
        private readonly ILogger<AppointmentReminderService> _logger;

        public AppointmentReminderService(
            IServiceProvider services,
            ILogger<AppointmentReminderService> logger)
        {
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AppointmentReminderService started");

            while (!stoppingToken.IsCancellationRequested)
            {
                // Calculate time until next 9:00 AM
                var now = DateTime.Now;
                var nextRun = now.Date.AddDays(1).AddHours(9);
                var delay = nextRun - now;

                // If it's before 9 AM today, run today at 9 AM
                if (now.Hour < 9)
                    delay = now.Date.AddHours(9) - now;

                _logger.LogInformation("Next reminder run at: {NextRun}", nextRun);

                try
                {
                    await Task.Delay(delay, stoppingToken);
                    await SendRemindersAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in AppointmentReminderService");
                }
            }
        }

        private async Task SendRemindersAsync(CancellationToken ct)
        {
            using var scope = _services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<RecruitmentCrmContext>();
            var notificationSvc = scope.ServiceProvider.GetRequiredService<INotificationService>();

            var tomorrow = DateTime.Today.AddDays(1);
            var today = DateTime.Today;

            // ── 1. Appointments tomorrow (Status = 5) ──────────────────────
            var tomorrowAppointments = await context.Leads
                .Where(l => l.Status == 5
                    && l.AppointmentDate.HasValue
                    && l.AppointmentDate.Value.Date == tomorrow
                    && l.AssignedSalesId.HasValue)
                .Select(l => new
                {
                    l.Id,
                    l.FullName,
                    l.Phone,
                    l.AppointmentDate,
                    SalesId = l.AssignedSalesId!.Value
                })
                .ToListAsync(ct);

            foreach (var lead in tomorrowAppointments)
            {
                var time = lead.AppointmentDate!.Value.ToString("HH:mm");
                await notificationSvc.SendAsync(
                    userId: lead.SalesId,
                    title: $"📅 تذكير: موعد غداً",
                    message: $"{lead.FullName} ({lead.Phone}) — غداً الساعة {time}. تذكر الاتصال لتأكيد الحضور",
                    link: $"/TeleSales/LeadDetail/{lead.Id}",
                    type: NotificationType.General
                );
            }

            _logger.LogInformation("Sent {Count} appointment reminders for tomorrow", tomorrowAppointments.Count);

            // ── 2. Due FollowUp Reminders today ────────────────────────────
            var dueReminders = await context.FollowUpReminders
                .Include(r => r.Lead)
                .Where(r => r.Status == 1
                    && r.ReminderDate <= DateOnly.FromDateTime(today)
                    && (r.SnoozedUntil == null || r.SnoozedUntil <= DateOnly.FromDateTime(today)))
                .Select(r => new
                {
                    r.Id,
                    r.AssignedToId,
                    LeadName = r.Lead != null ? r.Lead.FullName : null,
                    LeadPhone = r.Lead != null ? r.Lead.Phone : null,
                    LeadId = r.LeadId,
                    r.Notes
                })
                .ToListAsync(ct);

            foreach (var reminder in dueReminders)
            {
                // Build message — use Notes if no lead info available
                var msgTitle = "🔔 متابعة مستحقة اليوم";
                var msgBody = !string.IsNullOrEmpty(reminder.LeadName)
                    ? $"{reminder.LeadName} ({reminder.LeadPhone})" +
                      (string.IsNullOrEmpty(reminder.Notes) ? "" : $" — {reminder.Notes}")
                    : reminder.Notes ?? "لديك تذكير مستحق اليوم";

                // Link: Sales candidate link if no lead, else TeleSales lead link
                var link = reminder.LeadId != Guid.Empty && reminder.LeadName != null
                    ? $"/TeleSales/LeadDetail/{reminder.LeadId}"
                    : "/Sales/Candidates";

                await notificationSvc.SendAsync(
                    userId: reminder.AssignedToId,
                    title: msgTitle,
                    message: msgBody,
                    link: link,
                    type: NotificationType.General
                );
            }

            // Mark reminders as notified
            var reminderIds = dueReminders.Select(r => r.Id).ToList();
            if (reminderIds.Any())
            {
                await context.FollowUpReminders
                    .Where(r => reminderIds.Contains(r.Id))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.Status, 2)
                        .SetProperty(r => r.UpdatedAt, DateTime.UtcNow), ct);
            }

            _logger.LogInformation("Sent {Count} follow-up reminders for today", dueReminders.Count);
        }
    }
}
