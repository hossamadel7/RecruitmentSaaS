using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RecruitmentSaaS.Services;

public sealed record OfficeBookingRequest(long ExpectedOwnershipVersion, long ExpectedBookingVersion, Guid BranchId,
    DateTime ArrivalLocal, string Purpose, Guid ConfirmationMessageId, bool CustomerConfirmed, bool GeneralConsultationDisclosed);

public sealed class AiOfficeBookingService(RecruitmentCrmContext db, AiSalesControlService controls, IOptions<AiOfficeBookingOptions> options)
{
    public AiOfficeBookingOptions Options => options.Value;
    public async Task<AiOfficeBooking> SaveAsync(Guid chatId, Guid? bookingId, Guid actor, OfficeBookingRequest request, string key, CancellationToken ct)
    {
        controls.RequireEnabled();
        if (!Options.Enabled) throw new AiSalesValidation("Office booking is disabled.");
        if (!request.CustomerConfirmed || !request.GeneralConsultationDisclosed || string.IsNullOrWhiteSpace(request.Purpose) || request.Purpose.Length > 500)
            throw new AiSalesValidation("Customer confirmation, consultation disclosure and a visit purpose are required.");
        var fingerprint = Hash(JsonSerializer.Serialize(new { action = "OfficeSave", chatId, bookingId, actor, request }));
        ValidateKey(key);
        await using var requestGate = await ConversationLock.AcquireAsync(db, "request:" + controls.Options.TenantId + ":" + key, ct);
        var replay = await ReplayAsync(key, fingerprint, ct); if (replay != null) return replay;
        await using var chatGate = await ConversationLock.AcquireAsync(db, "conversation:" + chatId, ct);
        await using var policyGate = await ConversationLock.AcquireAsync(db, "office-policy:" + controls.Options.TenantId + ":" + request.BranchId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var chat = await ScopedChatAsync(chatId, request.ExpectedOwnershipVersion, ct, requireActive: true);
        var lead = await LockedLeadAsync(chat, ct);
        if (lead.IsConverted || lead.IsDuplicate || lead.Status is 6 or 7 or 8 or 9) throw new AiSalesValidation("This lead is not eligible for a new consultation.");
        AiOfficeBooking booking;
        if (bookingId == null)
        {
            if (request.ExpectedBookingVersion != 0 || lead.Status == 5 || lead.AppointmentDate != null ||
                await db.AiOfficeBookings.AnyAsync(b => b.LeadId == lead.Id && b.Status == AiOfficeBookingStatus.Confirmed, ct))
                throw new AiSalesConflict("An existing booking must be reviewed before a new booking.");
            var control = await db.ConversationControls.SingleAsync(c => c.ConversationId == chatId, ct);
            var agent = await db.AiSalesAgents.SingleOrDefaultAsync(a => a.Id == chat.AssignedAiSalesAgentId && a.TenantId == controls.Options.TenantId &&
                a.Mode == AiSalesMode.Shadow && !a.Paused && a.TeamManagerId == chat.PendingTeamManagerId && a.WhatsAppAccountId == chat.WhatsAppAccountId, ct);
            if (control.Controller != ConversationControllerMode.Assisted || agent == null) throw new AiSalesValidation("New AI bookings require a resumed Shadow conversation.");
            booking = new() { TenantId = controls.Options.TenantId, ConversationId = chatId, LeadId = lead.Id, AgentId = agent.Id, TeamManagerId = agent.TeamManagerId };
            db.AiOfficeBookings.Add(booking);
        }
        else
        {
            booking = await db.AiOfficeBookings.SingleOrDefaultAsync(b => b.Id == bookingId && b.ConversationId == chatId && b.TenantId == controls.Options.TenantId, ct)
                ?? throw new AiSalesValidation("Booking unavailable.");
            CheckBooking(booking, lead, request.ExpectedBookingVersion); booking.Version++;
        }
        var arrival = DateTime.SpecifyKind(request.ArrivalLocal, DateTimeKind.Unspecified);
        if (request.ArrivalLocal.Kind != DateTimeKind.Unspecified) throw new AiSalesValidation("Arrival must be Cairo local time without a UTC suffix or offset.");
        if (!EgyptTime.IsUnambiguousLocal(arrival)) throw new AiSalesValidation("Choose an unambiguous Cairo arrival time outside a clock-change gap.");
        DateTime utc;
        try { utc = EgyptTime.ToUtc(arrival); } catch (ArgumentException) { throw new AiSalesValidation("Arrival time is invalid in Cairo time."); }
        if (utc <= DateTime.UtcNow || arrival.Date > EgyptTime.Today.AddDays(Options.MaxDaysAhead) || arrival.Second != 0 || arrival.Millisecond != 0)
            throw new AiSalesValidation("Choose a future approximate arrival within the booking horizon, using whole minutes.");
        var policy = await db.AiOfficePolicies.SingleOrDefaultAsync(p => p.TenantId == controls.Options.TenantId && p.WhatsAppAccountId == chat.WhatsAppAccountId &&
            p.BranchId == request.BranchId && p.Enabled && p.ValidUntil > DateTime.UtcNow && db.Branches.Any(b => b.Id == p.BranchId && b.IsActive), ct)
            ?? throw new AiSalesValidation("Current approved office hours are required for this account and branch.");
        var minute = arrival.Hour * 60 + arrival.Minute;
        if (policy.ValidUntil <= utc || (policy.OpenDaysMask & (1 << (int)arrival.DayOfWeek)) == 0 || minute < policy.OpensAtMinute || minute >= policy.ClosesAtMinute)
            throw new AiSalesValidation("Arrival falls outside approved office hours or policy validity.");
        var message = await db.WhatsAppMessages.AsNoTracking().SingleOrDefaultAsync(m => m.Id == request.ConfirmationMessageId && m.ConversationId == chatId &&
            m.WhatsAppAccountId == chat.WhatsAppAccountId && m.Direction == (byte)MessageDirection.Incoming, ct)
            ?? throw new AiSalesValidation("Select the customer's confirmation message from this conversation.");
        var evidence = message.TextBody ?? ""; AiVoiceTurn? voice = null;
        if (message.MessageType == (byte)WhatsAppMessageType.Audio)
        {
            voice = await db.AiVoiceParts.Where(p => p.MessageId == message.Id).Join(db.AiVoiceTurns.Where(t => t.TenantId == controls.Options.TenantId &&
                t.ConversationId == chatId && t.Status == AiVoiceStatus.Reviewed), p => p.TurnId, t => t.Id, (p, t) => t).SingleOrDefaultAsync(ct);
            evidence = voice?.ReviewedText ?? "";
        }
        else if (message.MessageType != (byte)WhatsAppMessageType.Text) evidence = "";
        if (string.IsNullOrWhiteSpace(evidence) || message.WhatsAppTimestamp < DateTime.UtcNow.AddDays(-1) || message.WhatsAppTimestamp > DateTime.UtcNow.AddMinutes(1) ||
            // Legacy message timestamps are stored to whole seconds. Reject the previous ID explicitly and
            // require a one-second safety margin so rounding cannot make pre-booking evidence look fresh.
            bookingId != null && (message.Id == booking.ConfirmationMessageId || message.CreatedAt <= booking.UpdatedAt.AddSeconds(1)))
            throw new AiSalesValidation("A recent readable customer confirmation is required; rescheduling needs fresh confirmation.");
        await SettleReminderAsync(booking, actor, ct);
        booking.ArrivalLocal = arrival; booking.BranchId = policy.BranchId; booking.PolicyId = policy.Id; booking.PolicyVersion = policy.Version;
        booking.Purpose = request.Purpose.Trim(); booking.ConfirmationMessageId = message.Id; booking.ConfirmationVoiceTurnId = voice?.Id;
        booking.ConfirmationFingerprint = Hash(JsonSerializer.Serialize(new { message.Id, evidence, VoiceVersion = voice?.Version }));
        booking.ReviewedById = actor; booking.UpdatedAt = DateTime.UtcNow;
        var previous = lead.Status; lead.Status = 5; lead.AppointmentDate = arrival; lead.UpdatedAt = DateTime.UtcNow;
        chat.Status = (byte)ConversationStatus.Open; chat.LeadStage = (byte)LeadStage.Qualified; chat.UpdatedAt = DateTime.UtcNow;
        var reminder = new FollowUpReminder { Id = Guid.NewGuid(), LeadId = lead.Id, AssignedToId = actor, CreatedById = actor,
            ReminderDate = DateOnly.FromDateTime(arrival.AddDays(-1)), Status = 1, CreatedAt = DateTime.UtcNow,
            Notes = "استشارة مفتوحة — موعد تقريبي " + arrival.ToString("yyyy-MM-dd HH:mm") + " — حجز المساعد: " + booking.Id };
        db.FollowUpReminders.Add(reminder); booking.ReminderId = reminder.Id;
        var funnel = new LeadFunnelHistory { Id = Guid.NewGuid(), LeadId = lead.Id, FromStatus = previous, ToStatus = 5, ChangedById = actor,
            Note = "حجز استشارة مفتوحة للمساعد الافتراضي؛ راجعه الموظف. لا يوجد ضمان لوظيفة.", CreatedAt = DateTime.UtcNow };
        db.LeadFunnelHistories.Add(funnel);
        Event(booking, actor, bookingId == null ? "Confirmed" : "Rescheduled", $"Office policy {policy.Id} v{policy.Version}; customer confirmation reviewed; general consultation disclosed.", funnel.Id);
        db.AiControlRequests.Add(new() { TenantId = controls.Options.TenantId, Key = key, Fingerprint = fingerprint, ResourceId = booking.Id, Version = booking.Version });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return booking;
    }

    public async Task<AiOfficeBooking> CancelAsync(Guid id, Guid actor, long ownershipVersion, long version, string reason, string key, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500) throw new AiSalesValidation("A cancellation reason is required.");
        ValidateKey(key); var fingerprint = Hash(JsonSerializer.Serialize(new { action = "OfficeCancel", id, actor, ownershipVersion, version, reason }));
        await using var requestGate = await ConversationLock.AcquireAsync(db, "request:" + controls.Options.TenantId + ":" + key, ct);
        var replay = await ReplayAsync(key, fingerprint, ct); if (replay != null) return replay;
        var booking = await db.AiOfficeBookings.SingleOrDefaultAsync(b => b.Id == id && b.TenantId == controls.Options.TenantId, ct)
            ?? throw new AiSalesValidation("Booking unavailable.");
        await using var chatGate = await ConversationLock.AcquireAsync(db, "conversation:" + booking.ConversationId, ct);
        await db.Entry(booking).ReloadAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var chat = await ScopedChatAsync(booking.ConversationId, ownershipVersion, ct); var lead = await LockedLeadAsync(chat, ct);
        CheckBooking(booking, lead, version); await SettleReminderAsync(booking, actor, ct);
        booking.Status = AiOfficeBookingStatus.Cancelled; booking.Version++; booking.UpdatedAt = DateTime.UtcNow;
        lead.AppointmentDate = null; lead.Status = 4; lead.UpdatedAt = DateTime.UtcNow;
        var funnel = new LeadFunnelHistory { Id = Guid.NewGuid(), LeadId = lead.Id, FromStatus = 5, ToStatus = 4, ChangedById = actor, Note = "إلغاء استشارة المساعد الافتراضي", CreatedAt = DateTime.UtcNow };
        db.LeadFunnelHistories.Add(funnel); Event(booking, actor, "Cancelled", reason.Trim(), funnel.Id);
        db.AiControlRequests.Add(new() { TenantId = controls.Options.TenantId, Key = key, Fingerprint = fingerprint, ResourceId = id, Version = booking.Version });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return booking;
    }
    private async Task<WhatsAppConversation> ScopedChatAsync(Guid id, long version, CancellationToken ct, bool requireActive = false)
    {
        var control = await db.ConversationControls.AsNoTracking().SingleOrDefaultAsync(c => c.ConversationId == id && c.TenantId == controls.Options.TenantId, ct);
        if (control == null || control.OwnershipVersion != version) throw new AiSalesConflict("Conversation control changed; refresh first.");
        var chat = await db.WhatsAppConversations.SingleAsync(c => c.Id == id, ct);
        if (requireActive && !await db.WhatsAppAccounts.AnyAsync(a => a.Id == chat.WhatsAppAccountId && a.IsActive, ct)) throw new AiSalesValidation("Account unavailable.");
        return chat;
    }
    private async Task<Lead> LockedLeadAsync(WhatsAppConversation chat, CancellationToken ct)
    {
        if (chat.LeadId == null) throw new AiSalesValidation("A linked lead is required.");
        var lead = await db.Leads.FromSqlInterpolated($"SELECT * FROM demorecruitment.\"Leads\" WHERE \"Id\" = {chat.LeadId.Value} FOR UPDATE").SingleAsync(ct);
        await db.Entry(lead).ReloadAsync(ct); return lead;
    }
    private static void CheckBooking(AiOfficeBooking booking, Lead lead, long version)
    {
        if (booking.Version != version || booking.Status != AiOfficeBookingStatus.Confirmed || booking.LeadId != lead.Id ||
            lead.Status != 5 || lead.AppointmentDate != booking.ArrivalLocal || lead.IsConverted)
            throw new AiSalesConflict("Booking or CRM state changed; review it before proceeding.");
    }
    private async Task SettleReminderAsync(AiOfficeBooking booking, Guid actor, CancellationToken ct)
    {
        if (booking.ReminderId == null) return;
        var reminder = await db.FollowUpReminders.SingleAsync(r => r.Id == booking.ReminderId, ct);
        reminder.Status = 3; reminder.DismissedAt = DateTime.UtcNow; reminder.DismissedById = actor; reminder.UpdatedAt = DateTime.UtcNow;
    }
    private void Event(AiOfficeBooking booking, Guid actor, string action, string reason, Guid? funnel = null)
    {
        db.AiOfficeBookingEvents.Add(new() { TenantId = booking.TenantId, BookingId = booking.Id, Version = booking.Version, ActorId = actor,
            Action = action, Reason = reason, ArrivalLocal = booking.ArrivalLocal, BranchId = booking.BranchId, Purpose = booking.Purpose,
            ConfirmationMessageId = booking.ConfirmationMessageId, ConfirmationFingerprint = booking.ConfirmationFingerprint, FunnelHistoryId = funnel });
        db.AuditLogs.Add(new() { Id = Guid.NewGuid(), ActorId = actor, ActorType = 1, EventType = "AiOffice" + action, EntityType = "AiOfficeBooking", EntityId = booking.Id, CreatedAt = DateTime.UtcNow });
    }
    private async Task<AiOfficeBooking?> ReplayAsync(string key, string fingerprint, CancellationToken ct)
    {
        var row = await db.AiControlRequests.AsNoTracking().SingleOrDefaultAsync(r => r.TenantId == controls.Options.TenantId && r.Key == key, ct);
        if (row == null) return null;
        if (row.Fingerprint != fingerprint) throw new AiSalesConflict("Idempotency key belongs to another operation.");
        return await db.AiOfficeBookings.SingleAsync(b => b.Id == row.ResourceId && b.TenantId == row.TenantId, ct);
    }
    private static void ValidateKey(string key) { if (string.IsNullOrWhiteSpace(key) || key.Length > 128) throw new AiSalesValidation("Idempotency-Key is required (maximum 128 characters)."); }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    // Called only with a real reception visit, inside reception's transaction and lead lock.
    public async Task AttachVisitLockedAsync(LeadVisit visit, CancellationToken ct)
    {
        var localDay = EgyptTime.FromUtc(visit.VisitDateTime).Date;
        var booking = await db.AiOfficeBookings.SingleOrDefaultAsync(b => b.TenantId == controls.Options.TenantId && b.LeadId == visit.LeadId &&
            b.Status == AiOfficeBookingStatus.Confirmed && b.BranchId == visit.BranchId && b.ArrivalLocal >= localDay && b.ArrivalLocal < localDay.AddDays(1), ct);
        if (booking == null) return; // Missing, wrong-date or wrong-branch evidence is never a no-show.
        booking.Status = AiOfficeBookingStatus.Visited; booking.VerifiedVisitId = visit.Id; booking.Version++; booking.UpdatedAt = DateTime.UtcNow;
        await SettleReminderAsync(booking, visit.ReceptionUserId, ct); Event(booking, visit.ReceptionUserId, "VerifiedVisit", "Reception check-in evidence.");
    }
}
