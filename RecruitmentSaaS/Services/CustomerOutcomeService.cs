using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Services
{
    /// <summary>Egypt local time (the office, the bookings and the daily report all count days in Cairo time).</summary>
    public static class EgyptTime
    {
        private static readonly TimeZoneInfo Zone = Find();

        private static TimeZoneInfo Find()
        {
            foreach (var id in new[] { "Africa/Cairo", "Egypt Standard Time" })
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            return TimeZoneInfo.CreateCustomTimeZone("Cairo", TimeSpan.FromHours(2), "Cairo", "Cairo");
        }

        public static DateTime FromUtc(DateTime utc) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

        public static DateTime ToUtc(DateTime local) =>
            TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), Zone);

        public static DateTime Today => FromUtc(DateTime.UtcNow).Date;
    }

    /// <summary>
    /// The one "حالة العميل" choice TeleSales make on a chat. Each choice moves the chat and its lead together,
    /// using the lead statuses the rest of the system already knows (5 = موعد مجدول, 6 = زار المكتب, 8 = خسارة),
    /// so reception, office sales, reminders and the bookings report all see the same thing.
    /// </summary>
    public class CustomerOutcomeService
    {
        public const string Waiting = "waiting";
        public const string FollowUp = "followup";
        public const string Booked = "booked";
        public const string Visited = "visited";
        public const string Lost = "lost";

        private const byte LeadInterested = 4, LeadBooked = 5, LeadVisited = 6, LeadConverted = 7, LeadLost = 8;
        private const byte LeadSourceWhatsApp = 5;

        private static readonly Dictionary<byte, string> LeadStatusNames = new()
        {
            { 1, "جديد" }, { 2, "تم التواصل" }, { 3, "استجاب" }, { 4, "مهتم" }, { 5, "موعد مجدول" },
            { 6, "زار المكتب" }, { 7, "تم التحويل" }, { 8, "خسارة" }
        };

        private readonly RecruitmentCrmContext _context;

        public CustomerOutcomeService(RecruitmentCrmContext context)
        {
            _context = context;
        }

        /// <summary>What the dropdown shows for this chat right now ("" = nothing chosen yet).</summary>
        public static string Current(WhatsAppConversation c, Lead? lead)
        {
            if (lead?.Status == LeadLost || c.LeadStage == (byte)LeadStage.Lost) return Lost;
            if (lead?.Status is LeadVisited or LeadConverted) return Visited;
            if (lead?.Status == LeadBooked) return Booked;
            if (c.Status == (byte)ConversationStatus.FollowUp) return FollowUp;
            if (c.Status == (byte)ConversationStatus.WaitingForCustomer) return Waiting;
            return "";
        }

        /// <param name="at">متابعة: the moment (UTC). حجز: the appointment in Egypt time (as the leads pages store it).</param>
        /// <returns>An error message, or null when saved.</returns>
        public async Task<string?> ApplyAsync(WhatsAppConversation conversation, string outcome, DateTime? at, string? reason,
                                              Guid actorId, string actorName)
        {
            var now = DateTime.UtcNow;
            reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            if (reason?.Length > 500) reason = reason[..500];

            switch (outcome)
            {
                case Waiting:
                    conversation.Status = (byte)ConversationStatus.WaitingForCustomer;
                    break;

                case FollowUp:
                {
                    if (at == null) return "حدد يوم ووقت المتابعة";
                    var dueUtc = at.Value.Kind == DateTimeKind.Local ? at.Value.ToUniversalTime() : at.Value;
                    if (dueUtc < now.AddMinutes(-5)) return "ميعاد المتابعة لازم يكون بعد دلوقتي";
                    conversation.Status = (byte)ConversationStatus.FollowUp;
                    _context.ConversationFollowUps.Add(new ConversationFollowUp
                    {
                        Id = Guid.NewGuid(),
                        ConversationId = conversation.Id,
                        AssignedToId = conversation.AssignedSalesAgentId ?? actorId,
                        CreatedById = actorId,
                        DueAt = DateTime.SpecifyKind(dueUtc, DateTimeKind.Unspecified),
                        Status = (byte)FollowUpStatus.Pending,
                        Notes = reason,
                        CreatedAt = now
                    });
                    var lead = await LeadOfAsync(conversation, create: false);
                    if (lead != null && lead.Status < LeadInterested)
                        ChangeLeadStatus(lead, LeadInterested, actorId, actorName, null, now);
                    break;
                }

                case Booked:
                {
                    if (at == null) return "حدد يوم ووقت الحجز";
                    var appointment = DateTime.SpecifyKind(at.Value, DateTimeKind.Unspecified);
                    if (appointment.Date < EgyptTime.Today) return "يوم الحجز لازم يكون النهارده أو بعد كده";
                    var lead = await LeadOfAsync(conversation, create: true);
                    if (lead == null) return "مش قادر أسجل العميل كليد (مفيش فرع أو أدمن نشط)";
                    var changed = lead.Status != LeadBooked || lead.AppointmentDate != appointment;
                    lead.AppointmentDate = appointment;
                    if (changed)
                    {
                        ChangeLeadStatus(lead, LeadBooked, actorId, actorName,
                            $"الموعد: {appointment:dd/MM/yyyy hh:mm tt}" + (reason != null ? $" · {reason}" : ""), now);
                        // Reminder the day before (the daily 9 AM reminder service also picks up status 5)
                        _context.FollowUpReminders.Add(new FollowUpReminder
                        {
                            Id = Guid.NewGuid(),
                            LeadId = lead.Id,
                            AssignedToId = lead.AssignedSalesId ?? actorId,
                            CreatedById = actorId,
                            ReminderDate = DateOnly.FromDateTime(appointment.AddDays(-1)),
                            Notes = $"تذكير: موعد {lead.FullName} في المكتب {appointment:dd/MM} الساعة {appointment:hh:mm tt}",
                            Status = 1,
                            CreatedAt = now
                        });
                    }
                    conversation.Status = (byte)ConversationStatus.Open;
                    conversation.LeadStage = (byte)LeadStage.Qualified;
                    conversation.LostReason = null;
                    break;
                }

                case Visited:
                {
                    var lead = await LeadOfAsync(conversation, create: true);
                    if (lead == null) return "مش قادر أسجل العميل كليد (مفيش فرع أو أدمن نشط)";
                    if (lead.Status != LeadVisited && lead.Status != LeadConverted)
                        ChangeLeadStatus(lead, LeadVisited, actorId, actorName, "حضر للمكتب — سجّلها " + actorName, now);
                    conversation.Status = (byte)ConversationStatus.Open;
                    conversation.LeadStage = (byte)LeadStage.Qualified;
                    conversation.LostReason = null;
                    break;
                }

                case Lost:
                {
                    if (reason == null) return "اكتب سبب الخسارة";
                    var lead = await LeadOfAsync(conversation, create: false);
                    if (lead != null && lead.Status != LeadLost)
                        ChangeLeadStatus(lead, LeadLost, actorId, actorName, reason, now);
                    conversation.LeadStage = (byte)LeadStage.Lost;
                    conversation.LostReason = reason;
                    conversation.Status = (byte)ConversationStatus.Closed;
                    conversation.ClosedAt = now;
                    break;
                }

                default:
                    return "اختيار غير معروف";
            }

            conversation.UpdatedAt = now;
            await _context.SaveChangesAsync();
            return null;
        }

        // The chat's lead; for a booking we register the customer as a lead if they aren't one yet
        private async Task<Lead?> LeadOfAsync(WhatsAppConversation conversation, bool create)
        {
            if (conversation.LeadId != null)
                return await _context.Leads.FirstOrDefaultAsync(l => l.Id == conversation.LeadId);

            var contact = conversation.Contact ?? await _context.WhatsAppContacts.FirstAsync(c => c.Id == conversation.ContactId);
            var phone = PhoneNumbers.Normalize(contact.WhatsAppPhoneNumber);
            var variants = PhoneNumbers.StoredVariants(phone);
            var lead = await _context.Leads.FirstOrDefaultAsync(l => variants.Contains(l.Phone));
            if (lead == null && create)
            {
                var branchId = await _context.Branches.Where(b => b.IsActive).OrderBy(b => b.Name).Select(b => (Guid?)b.Id).FirstOrDefaultAsync();
                var adminId = await _context.Users.Where(u => u.Role == 1 && u.IsActive).OrderBy(u => u.CreatedAt).Select(u => (Guid?)u.Id).FirstOrDefaultAsync();
                if (branchId == null || adminId == null) return null;
                var agent = conversation.AssignedSalesAgentId;
                var team = agent == null ? null
                    : await _context.Users.Where(u => u.Id == agent).Select(u => u.ManagerId).FirstOrDefaultAsync();
                lead = new Lead
                {
                    Id = Guid.NewGuid(),
                    BranchId = branchId.Value,
                    RegisteredById = adminId.Value,
                    AssignedSalesId = agent,
                    FullName = conversation.IntakeName ?? contact.Name ?? phone,
                    Phone = phone,
                    LeadSource = LeadSourceWhatsApp,
                    Status = 1,
                    Age = conversation.IntakeAge,
                    InterestedJobTitle = conversation.IntakeJob,
                    Notes = "اتسجل من محادثة واتساب",
                    TeamManagerId = team,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Leads.Add(lead);
            }
            if (lead != null) conversation.LeadId = lead.Id;
            return lead;
        }

        private void ChangeLeadStatus(Lead lead, byte newStatus, Guid actorId, string actorName, string? note, DateTime now)
        {
            var old = lead.Status;
            lead.Status = newStatus;
            lead.UpdatedAt = now;
            lead.LastContactedAt = now;

            var description = $"تغيير الحالة من {LeadStatusNames.GetValueOrDefault(old, "—")} إلى {LeadStatusNames.GetValueOrDefault(newStatus, "—")} (من الواتساب)";
            if (note != null) description += $" — {note}";
            _context.LeadActivities.Add(new LeadActivity
            {
                Id = Guid.NewGuid(),
                LeadId = lead.Id,
                ActivityType = 4,
                Description = description.Length > 1000 ? description[..1000] : description,
                CreatedById = actorId,
                CreatedByName = actorName,
                ActorType = 1,
                CreatedAt = now
            });
            // The bookings report counts these rows: who booked (ToStatus 5) and who came (ToStatus 6), and when
            _context.LeadFunnelHistories.Add(new LeadFunnelHistory
            {
                Id = Guid.NewGuid(),
                LeadId = lead.Id,
                FromStatus = old,
                ToStatus = newStatus,
                ChangedById = actorId,
                Note = note?.Length > 500 ? note[..500] : note,
                CreatedAt = now
            });
        }
    }
}
