using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.DTOs;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.Security.Claims;

namespace RecruitmentSaaS.Controllers
{
    [Authorize(Roles = "3")]
    public class TeleSalesController : Controller
    {
        private readonly RecruitmentCrmContext _context;
        private readonly INotificationService _notifications;

        public TeleSalesController(RecruitmentCrmContext context, INotificationService notifications)
        {
            _context = context;
            _notifications = notifications;
        }

        private Guid CurrentUserId =>
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        private string CurrentUserName =>
            User.FindFirstValue(ClaimTypes.Name) ?? "مستخدم";

        // ── GET /TeleSales/Index ──────────────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            var userId = CurrentUserId;
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var now = DateTime.UtcNow;

            var dto = new SalesDashboardDto
            {
                MyLeadsTotal = await _context.Leads
                    .CountAsync(l => l.AssignedSalesId == userId),

                MyLeadsNew = await _context.Leads
                    .CountAsync(l => l.AssignedSalesId == userId && l.Status == 1),

                MyCandidatesTotal = 0, // Tele Sales doesn't manage candidates

                DealsThisMonth = await _context.Leads
                    .CountAsync(l => l.AssignedSalesId == userId
                        && l.IsConverted == true
                        && l.ConvertedAt != null
                        && l.ConvertedAt.Value.Month == now.Month
                        && l.ConvertedAt.Value.Year == now.Year),

                TodayReminders = await _context.FollowUpReminders
                    .Include(r => r.Lead)
                    .Where(r => r.AssignedToId == userId
                        && r.Status == 1
                        && r.ReminderDate <= today)
                    .Select(r => new FollowUpReminderDto
                    {
                        Id = r.Id,
                        LeadId = r.LeadId,
                        LeadName = r.Lead.FullName,
                        LeadPhone = r.Lead.Phone,
                        ReminderDate = r.ReminderDate,
                        Status = r.Status,
                        Notes = r.Notes
                    })
                    .ToListAsync(),

                RecentLeads = await _context.Leads
                    .Where(l => l.AssignedSalesId == userId)
                    .OrderByDescending(l => l.CreatedAt)
                    .Take(10)
                    .Select(l => new LeadListItemDto
                    {
                        Id = l.Id,
                        LeadCode = l.LeadCode ?? string.Empty,
                        FullName = l.FullName,
                        Phone = l.Phone,
                        LeadSource = l.LeadSource,
                        Status = l.Status,
                        IsConverted = l.IsConverted,
                        CreatedAt = l.CreatedAt
                    })
                    .ToListAsync()
            };

            return View(dto);
        }

        // ── GET /TeleSales/Pool ───────────────────────────────────────────────
        // Unassigned leads from campaigns this tele sales is assigned to
        public async Task<IActionResult> Pool(Guid? campaignId, int page = 1)
        {
            const int pageSize = 20;

            var query = _context.Leads
                .Include(l => l.Campaign)
                .Where(l => l.AssignedSalesId == null
                    && l.IsConverted == false
                    && l.IsDuplicate == false);

            if (campaignId.HasValue)
                query = query.Where(l => l.CampaignId == campaignId.Value);

            var totalCount = await query.CountAsync();

            var leads = await query
                .OrderByDescending(l => l.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(l => new LeadListItemDto
                {
                    Id = l.Id,
                    LeadCode = l.LeadCode ?? string.Empty,
                    FullName = l.FullName,
                    Phone = l.Phone,
                    LeadSource = l.LeadSource,
                    Status = l.Status,
                    IsConverted = l.IsConverted,
                    CreatedAt = l.CreatedAt,
                    CampaignName = l.Campaign != null ? l.Campaign.Name : null
                })
                .ToListAsync();

            // Load campaigns for filter
            var campaigns = await _context.Campaigns
                .Where(c => c.Status == 1)
                .OrderBy(c => c.Name)
                .Select(c => new CampaignListItemDto
                {
                    Id = c.Id,
                    Name = c.Name
                })
                .ToListAsync();

            ViewBag.Campaigns = campaigns;
            ViewBag.CurrentCampaignId = campaignId;
            ViewBag.CurrentPage = page;
            ViewBag.TotalCount = totalCount;

            return View(leads);
        }

        // ── POST /TeleSales/AssignToMe ────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignToMe(Guid leadId)
        {
            var userId = CurrentUserId;

            var lead = await _context.Leads
                .FirstOrDefaultAsync(l => l.Id == leadId
                    && l.AssignedSalesId == null
                    && l.IsConverted == false);

            if (lead == null)
            {
                TempData["Error"] = "هذا العميل غير متاح أو تم تعيينه بالفعل";
                return RedirectToAction("Pool");
            }

            lead.AssignedSalesId = userId;
            lead.UpdatedAt = DateTime.UtcNow;

            _context.LeadActivities.Add(new LeadActivity
            {
                Id = Guid.NewGuid(),
                LeadId = leadId,
                ActivityType = 8,
                Description = $"تم تعيين العميل لـ {CurrentUserName}",
                CreatedById = userId,
                CreatedByName = CurrentUserName,
                ActorType = 1,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            TempData["Success"] = "تم تعيين العميل لك بنجاح";
            return RedirectToAction("LeadDetail", new { id = leadId });
        }

        // ── GET /TeleSales/Leads ──────────────────────────────────────────────
        public async Task<IActionResult> Leads(byte? status, int page = 1)
        {
            var userId = CurrentUserId;
            const int pageSize = 20;

            var query = _context.Leads
                .Where(l => l.AssignedSalesId == userId);

            if (status.HasValue)
                query = query.Where(l => l.Status == status.Value);

            var totalCount = await query.CountAsync();

            var leads = await query
                .OrderByDescending(l => l.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(l => new LeadListItemDto
                {
                    Id = l.Id,
                    LeadCode = l.LeadCode ?? string.Empty,
                    FullName = l.FullName,
                    Phone = l.Phone,
                    LeadSource = l.LeadSource,
                    Status = l.Status,
                    IsConverted = l.IsConverted,
                    CreatedAt = l.CreatedAt,
                    LastContactedAt = l.LastContactedAt
                })
                .ToListAsync();

            ViewBag.CurrentStatus = status;
            ViewBag.CurrentPage = page;
            ViewBag.TotalCount = totalCount;

            return View(leads);
        }

        // ── GET /TeleSales/LeadDetail/{id} ────────────────────────────────────
        public async Task<IActionResult> LeadDetail(Guid id)
        {
            var userId = CurrentUserId;

            // Allow viewing unassigned leads too (from pool)
            var lead = await _context.Leads
                .Include(l => l.AssignedSales)
                .Include(l => l.Campaign)
                .Include(l => l.RegisteredBy)
                .FirstOrDefaultAsync(l => l.Id == id
                    && (l.AssignedSalesId == userId || l.AssignedSalesId == null));

            if (lead == null) return NotFound();

            var activities = await _context.LeadActivities
                .Where(a => a.LeadId == id)
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            var reminders = await _context.FollowUpReminders
                .Where(r => r.LeadId == id && r.Status == 1)
                .ToListAsync();

            ViewBag.IsAssignedToMe = lead.AssignedSalesId == userId;

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

                Reminders = reminders.Select(r => new FollowUpReminderDto
                {
                    Id = r.Id,
                    LeadId = r.LeadId,
                    LeadName = lead.FullName,
                    LeadPhone = lead.Phone,
                    ReminderDate = r.ReminderDate,
                    Status = r.Status,
                    Notes = r.Notes
                }).ToList()
            };

            return View(dto);
        }

        // ── POST /TeleSales/UpdateStatus ──────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(Guid leadId, byte newStatus, DateTime? appointmentDate = null)
        {
            var userId = CurrentUserId;

            // Tele Sales can only set status 2-5 and 8
            if (!new byte[] { 2, 3, 4, 5, 8 }.Contains(newStatus))
            {
                TempData["Error"] = "غير مسموح بهذه الحالة";
                return RedirectToAction("LeadDetail", new { id = leadId });
            }

            // Status 5 requires appointment date
            if (newStatus == 5 && appointmentDate == null)
            {
                TempData["Error"] = "يرجى تحديد تاريخ ووقت الموعد";
                return RedirectToAction("LeadDetail", new { id = leadId });
            }

            var lead = await _context.Leads
                .FirstOrDefaultAsync(l => l.Id == leadId && l.AssignedSalesId == userId);

            if (lead == null)
            {
                TempData["Error"] = "العميل غير موجود";
                return RedirectToAction("Leads");
            }

            var oldStatus = lead.Status;
            lead.Status = newStatus;
            lead.UpdatedAt = DateTime.UtcNow;

            // Save appointment date and schedule reminder
            if (newStatus == 5 && appointmentDate.HasValue)
            {
                lead.AppointmentDate = appointmentDate.Value;

                // Create follow-up reminder for the day before
                var reminderDate = DateOnly.FromDateTime(appointmentDate.Value.AddDays(-1));
                _context.FollowUpReminders.Add(new FollowUpReminder
                {
                    Id = Guid.NewGuid(),
                    LeadId = leadId,
                    AssignedToId = userId,
                    CreatedById = userId,
                    ReminderDate = reminderDate,
                    Notes = $"تذكير: موعد {lead.FullName} غداً الساعة {appointmentDate.Value:HH:mm}",
                    Status = 1,
                    CreatedAt = DateTime.UtcNow
                });

                // Send immediate notification to confirm appointment was set
                await _notifications.SendAsync(
                    userId: userId,
                    title: $"📅 تم تحديد موعد: {lead.FullName}",
                    message: $"الموعد: {appointmentDate.Value:dd/MM/yyyy HH:mm} · ستصلك تذكرة يوم {reminderDate:dd/MM/yyyy}",
                    link: $"/TeleSales/LeadDetail/{leadId}",
                    type: NotificationType.General
                );
            }

            var statusNames = new Dictionary<byte, string>
            {
                {1,"جديد"},{2,"تم التواصل"},{3,"استجاب"},
                {4,"مهتم"},{5,"موعد مجدول"},{6,"زار المكتب"},
                {7,"تم التحويل"},{8,"خسارة"}
            };

            _context.LeadActivities.Add(new LeadActivity
            {
                Id = Guid.NewGuid(),
                LeadId = leadId,
                ActivityType = 4,
                Description = $"تغيير الحالة من {statusNames.GetValueOrDefault(oldStatus, oldStatus.ToString())} إلى {statusNames.GetValueOrDefault(newStatus, newStatus.ToString())}",
                CreatedById = userId,
                CreatedByName = CurrentUserName,
                ActorType = 1,
                CreatedAt = DateTime.UtcNow
            });

            _context.LeadFunnelHistories.Add(new LeadFunnelHistory
            {
                Id = Guid.NewGuid(),
                LeadId = leadId,
                FromStatus = oldStatus,
                ToStatus = newStatus,
                ChangedById = userId,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            TempData["Success"] = "تم تحديث الحالة بنجاح";
            return RedirectToAction("LeadDetail", new { id = leadId });
        }

        // ── POST /TeleSales/LogCall ───────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LogCall(LogCallDto dto)
        {
            var userId = CurrentUserId;

            _context.LeadCallLogs.Add(new LeadCallLog
            {
                Id = Guid.NewGuid(),
                LeadId = dto.LeadId,
                CalledById = userId,
                Channel = dto.Channel,
                Outcome = dto.Outcome,
                Note = dto.Note,
                CalledAt = DateTime.UtcNow
            });

            var lead = await _context.Leads.FindAsync(dto.LeadId);
            if (lead != null)
            {
                lead.LastContactedAt = DateTime.UtcNow;
                // Auto set status to 2 if still new
                if (lead.Status == 1 && dto.Outcome == 2)
                {
                    lead.Status = 2;
                    _context.LeadActivities.Add(new LeadActivity
                    {
                        Id = Guid.NewGuid(),
                        LeadId = dto.LeadId,
                        ActivityType = 4,
                        Description = "تغيير الحالة من جديد إلى تم التواصل",
                        CreatedById = userId,
                        CreatedByName = CurrentUserName,
                        ActorType = 1,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            _context.LeadActivities.Add(new LeadActivity
            {
                Id = Guid.NewGuid(),
                LeadId = dto.LeadId,
                ActivityType = 6,
                Description = $"مكالمة — القناة: {(dto.Channel == 1 ? "هاتف" : "واتساب")} · النتيجة: {dto.Outcome switch { 1 => "لا يرد", 2 => "أجاب", 3 => "سيرد لاحقاً", 4 => "غير مهتم", 5 => "مهتم", _ => "" }}" + (string.IsNullOrEmpty(dto.Note) ? "" : $" · {dto.Note}"),
                CreatedById = userId,
                CreatedByName = CurrentUserName,
                ActorType = 1,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            // Notify telesales if they set a follow-up date
            if (dto.NextFollowUpDate.HasValue && lead != null)
            {
                await _notifications.SendAsync(
                    userId: userId,
                    title: $"🔔 متابعة مجدولة: {lead.FullName}",
                    message: $"تذكير متابعة بتاريخ {dto.NextFollowUpDate.Value:dd/MM/yyyy} · {lead.Phone}",
                    link: $"/TeleSales/LeadDetail/{dto.LeadId}",
                    type: NotificationType.General
                );
            }

            TempData["Success"] = "تم تسجيل المكالمة بنجاح";
            return RedirectToAction("LeadDetail", new { id = dto.LeadId });
        }

        // ── POST /TeleSales/AddNote ───────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddNote(AddNoteDto dto)
        {
            var userId = CurrentUserId;

            _context.LeadActivities.Add(new LeadActivity
            {
                Id = Guid.NewGuid(),
                LeadId = dto.LeadId,
                ActivityType = 1,
                Description = dto.Description,
                NextFollowUpDate = dto.NextFollowUpDate,
                CreatedById = userId,
                CreatedByName = CurrentUserName,
                ActorType = 1,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            TempData["Success"] = "تمت إضافة الملاحظة بنجاح";
            return RedirectToAction("LeadDetail", new { id = dto.LeadId });
        }

        // ── POST /TeleSales/SetReminder ───────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetReminder(CreateReminderDto dto)
        {
            var userId = CurrentUserId;

            _context.FollowUpReminders.Add(new FollowUpReminder
            {
                Id = Guid.NewGuid(),
                LeadId = dto.LeadId,
                AssignedToId = userId,
                CreatedById = userId,
                ReminderDate = dto.ReminderDate,
                Notes = dto.Notes,
                Status = 1,
                CreatedAt = DateTime.UtcNow
            });

            var reminderLead = await _context.Leads
                .Where(l => l.Id == dto.LeadId)
                .Select(l => new { l.FullName, l.Phone })
                .FirstOrDefaultAsync();

            await _context.SaveChangesAsync();

            // Notify telesales confirming the reminder was set
            if (reminderLead != null)
            {
                await _notifications.SendAsync(
                    userId: userId,
                    title: $"🔔 تذكير مضاف: {reminderLead.FullName}",
                    message: $"ستصلك تذكرة متابعة {reminderLead.FullName} ({reminderLead.Phone}) بتاريخ {dto.ReminderDate:dd/MM/yyyy}" +
                             (string.IsNullOrEmpty(dto.Notes) ? "" : $" · {dto.Notes}"),
                    link: $"/TeleSales/LeadDetail/{dto.LeadId}",
                    type: NotificationType.General
                );
            }

            TempData["Success"] = "تم تعيين التذكير بنجاح";
            return RedirectToAction("LeadDetail", new { id = dto.LeadId });
        }

        // ── POST /TeleSales/DismissReminder ──────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DismissReminder(Guid reminderId, Guid leadId)
        {
            var reminder = await _context.FollowUpReminders.FindAsync(reminderId);
            if (reminder != null)
            {
                reminder.Status = 3;
                reminder.DismissedAt = DateTime.UtcNow;
                reminder.DismissedById = CurrentUserId;
                reminder.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            TempData["Success"] = "تم تجاهل التذكير";
            return RedirectToAction("LeadDetail", new { id = leadId });
        }
    }
}
