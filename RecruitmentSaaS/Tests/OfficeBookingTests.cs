using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using RecruitmentSaaS.Controllers;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.DTOs;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Xunit;

namespace RecruitmentSaaS.Tests;
public sealed partial class FoundationTests
{
    private static AiOfficeBookingService Office(RecruitmentCrmContext db, Seed s, bool enabled = true) => new(db, Control(db, s), Options.Create(new AiOfficeBookingOptions { Enabled = enabled }));
    private async Task<(AiOfficePolicy Policy, Guid Message, DateTime Arrival)> OfficeSeedAsync(Seed s)
    {
        await EngineSeedAsync(s); await using var db = fixture.Create();
        var chat = (await db.WhatsAppConversations.FindAsync(s.Chat))!; var lead = (await db.Leads.FindAsync(s.Lead))!;
        var policy = new AiOfficePolicy { TenantId = s.Tenant, WhatsAppAccountId = chat.WhatsAppAccountId, BranchId = lead.BranchId,
            OpenDaysMask = 127, OpensAtMinute = 0, ClosesAtMinute = 1440, Enabled = true, ValidUntil = DateTime.UtcNow.AddDays(60), ApprovedById = s.Admin,
            SourceReference = "Synthetic office hours only" };
        db.AiOfficePolicies.Add(policy); await db.SaveChangesAsync();
        return (policy, await ConfirmationAsync(s), DateTime.SpecifyKind(EgyptTime.Today.AddDays(2).AddHours(12), DateTimeKind.Unspecified));
    }
    private async Task<Guid> ConfirmationAsync(Seed s, DateTime? at = null)
    {
        await using var db = fixture.Create(); var chat = (await db.WhatsAppConversations.FindAsync(s.Chat))!;
        var message = new WhatsAppMessage { Id = Guid.NewGuid(), ConversationId = s.Chat, WhatsAppAccountId = chat.WhatsAppAccountId,
            Direction = (byte)MessageDirection.Incoming, MessageType = (byte)WhatsAppMessageType.Text, Status = (byte)MessageStatus.Received,
            TextBody = "ايوه موافق على الفرع واليوم والوصول التقريبي لاستشارة عامة من غير ضمان وظيفة", MessageSource = (byte)MessageSource.Customer,
            CreatedAt = at ?? DateTime.UtcNow, WhatsAppTimestamp = at ?? DateTime.UtcNow };
        db.WhatsAppMessages.Add(message); await db.SaveChangesAsync(); return message.Id;
    }
    private static OfficeBookingRequest Request((AiOfficePolicy Policy, Guid Message, DateTime Arrival) seed) =>
        new(1, 0, seed.Policy.BranchId, seed.Arrival, "استشارة عامة", seed.Message, true, true);

    [Fact]
    public async Task Booking_is_atomic_idempotent_preserves_AI_assignment_and_stales_drafts()
    {
        var s = await SeedAsync(); var seed = await OfficeSeedAsync(s); await using var db = fixture.Create();
        var fact = await db.AiSalesKnowledge.SingleAsync(k => k.TenantId == s.Tenant); var draft = await Engine(db, s, GoodProvider(fact.Id).Object).GenerateAsync(s.Chat, s.Admin, 1, "before-booking", default);
        var service = Office(db, s); var request = Request(seed);
        var booking = await service.SaveAsync(s.Chat, null, s.Admin, request, "book-once", default);
        Assert.Equal(booking.Id, (await service.SaveAsync(s.Chat, null, s.Admin, request, "book-once", default)).Id);
        Assert.Equal((byte)5, (await db.Leads.FindAsync(s.Lead))!.Status); Assert.Equal(seed.Arrival, (await db.Leads.FindAsync(s.Lead))!.AppointmentDate);
        Assert.Equal(s.Agent, (await db.Leads.FindAsync(s.Lead))!.AssignedAiSalesAgentId); Assert.Null((await db.Leads.FindAsync(s.Lead))!.AssignedSalesId);
        Assert.Single(await db.AiOfficeBookingEvents.Where(e => e.BookingId == booking.Id).ToListAsync()); Assert.NotNull(booking.ReminderId);
        Assert.False(await Engine(db, s, GoodProvider(fact.Id).Object).IsCurrentAsync(draft, default));
        await Assert.ThrowsAsync<AiSalesConflict>(() => service.SaveAsync(s.Chat, null, s.Admin, request with { Purpose = "Changed" }, "book-once", default));
        Assert.False(await db.WhatsAppMessages.AnyAsync(m => m.ConversationId == s.Chat && m.Direction == (byte)MessageDirection.Outgoing));
    }
    [Fact]
    public async Task Customer_confirmation_disclosure_hours_and_source_scope_are_required_before_any_write()
    {
        var s = await SeedAsync(); var seed = await OfficeSeedAsync(s); var other = await SeedAsync(); var foreign = await ConfirmationAsync(other);
        foreach (var request in new[] { Request(seed) with { CustomerConfirmed = false }, Request(seed) with { GeneralConsultationDisclosed = false },
            Request(seed) with { ConfirmationMessageId = foreign }, Request(seed) with { ArrivalLocal = seed.Arrival.AddDays(-100) } })
        {
            await using var db = fixture.Create(); await Assert.ThrowsAsync<AiSalesValidation>(() => Office(db, s).SaveAsync(s.Chat, null, s.Admin, request, Guid.NewGuid().ToString(), default));
        }
        await using (var db = fixture.Create())
        {
            var policy = (await db.AiOfficePolicies.FindAsync(seed.Policy.Id))!; policy.OpenDaysMask = 1 << ((int)seed.Arrival.DayOfWeek + 1) % 7; await db.SaveChangesAsync();
            await Assert.ThrowsAsync<AiSalesValidation>(() => Office(db, s).SaveAsync(s.Chat, null, s.Admin, Request(seed), "closed-day", default));
        }
        await using var verify = fixture.Create(); Assert.False(await verify.AiOfficeBookings.AnyAsync(b => b.ConversationId == s.Chat));
        Assert.Null((await verify.Leads.FindAsync(s.Lead))!.AppointmentDate); Assert.Equal((byte)1, (await verify.Leads.FindAsync(s.Lead))!.Status);
    }
    [Fact]
    public async Task Reschedule_replaces_reminder_requires_new_confirmation_and_keeps_booking_history()
    {
        var s = await SeedAsync(); var seed = await OfficeSeedAsync(s); await using var db = fixture.Create(); var service = Office(db, s);
        var booking = await service.SaveAsync(s.Chat, null, s.Admin, Request(seed), "book-reschedule", default); var oldReminder = booking.ReminderId;
        await using (var invalidDb = fixture.Create()) await Assert.ThrowsAsync<AiSalesValidation>(() => Office(invalidDb, s).SaveAsync(s.Chat, booking.Id, s.Admin,
            Request(seed) with { ExpectedBookingVersion = 1, ArrivalLocal = seed.Arrival.AddDays(1) }, "old-confirmation", default));
        var confirmation = await ConfirmationAsync(s, booking.UpdatedAt.AddSeconds(2));
        booking = await service.SaveAsync(s.Chat, booking.Id, s.Admin, Request(seed) with { ExpectedBookingVersion = 1, ArrivalLocal = seed.Arrival.AddDays(1), ConfirmationMessageId = confirmation }, "reschedule", default);
        Assert.Equal(2, booking.Version); Assert.Equal((byte)3, (await db.FollowUpReminders.FindAsync(oldReminder))!.Status);
        Assert.NotEqual(oldReminder, booking.ReminderId); Assert.Equal(seed.Arrival.AddDays(1), (await db.Leads.FindAsync(s.Lead))!.AppointmentDate);
        Assert.Equal(new[] { "Confirmed", "Rescheduled" }, await db.AiOfficeBookingEvents.Where(e => e.BookingId == booking.Id).OrderBy(e => e.Version).Select(e => e.Action).ToArrayAsync());
    }
    [Fact]
    public async Task Cancellation_after_takeover_works_when_feature_disabled_and_is_idempotent()
    {
        var s = await SeedAsync(); var seed = await OfficeSeedAsync(s); await using var db = fixture.Create();
        var booking = await Office(db, s).SaveAsync(s.Chat, null, s.Admin, Request(seed), "cancel-book", default);
        await Control(db, s).ControlAsync(s.Chat, s.Admin, 1, "takeover", "Customer cancellation", "booking-takeover", default);
        var service = Office(db, s, false); await service.CancelAsync(booking.Id, s.Admin, 2, 1, "Customer cancelled", "cancel", default);
        await service.CancelAsync(booking.Id, s.Admin, 2, 1, "Customer cancelled", "cancel", default);
        Assert.Equal(AiOfficeBookingStatus.Cancelled, booking.Status); Assert.Null((await db.Leads.FindAsync(s.Lead))!.AppointmentDate);
        Assert.Equal((byte)4, (await db.Leads.FindAsync(s.Lead))!.Status); Assert.Equal((byte)3, (await db.FollowUpReminders.FindAsync(booking.ReminderId))!.Status);
        Assert.Equal(2, await db.AiOfficeBookingEvents.CountAsync(e => e.BookingId == booking.Id));
    }
    [Fact]
    public async Task Booking_does_not_overwrite_legacy_appointments_and_stale_control_is_rejected()
    {
        var s = await SeedAsync(); var seed = await OfficeSeedAsync(s); await using var db = fixture.Create();
        await Assert.ThrowsAsync<AiSalesConflict>(() => Office(db, s).SaveAsync(s.Chat, null, s.Admin, Request(seed) with { ExpectedOwnershipVersion = 0 }, "stale-office", default));
        var lead = (await db.Leads.FindAsync(s.Lead))!; lead.AppointmentDate = seed.Arrival; lead.Status = 5; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<AiSalesConflict>(() => Office(db, s).SaveAsync(s.Chat, null, s.Admin, Request(seed), "legacy-office", default));
        Assert.False(await db.AiOfficeBookings.AnyAsync(b => b.ConversationId == s.Chat));
    }
    [Fact]
    public async Task Verified_reception_visit_is_attached_only_to_matching_day_and_branch()
    {
        var s = await SeedAsync(); var seed = await OfficeSeedAsync(s); await using var db = fixture.Create(); var service = Office(db, s);
        var booking = await service.SaveAsync(s.Chat, null, s.Admin, Request(seed), "reception-book", default);
        var visit = new LeadVisit { Id = Guid.NewGuid(), LeadId = s.Lead, BranchId = seed.Policy.BranchId, ReceptionUserId = s.Admin,
            MeetingOutcome = 1, VisitDateTime = EgyptTime.ToUtc(seed.Arrival.AddDays(-1)), CreatedAt = DateTime.UtcNow };
        db.LeadVisits.Add(visit); await db.SaveChangesAsync();
        await using (var tx = await db.Database.BeginTransactionAsync()) { await service.AttachVisitLockedAsync(visit, default); await db.SaveChangesAsync(); await tx.CommitAsync(); }
        Assert.Null(booking.VerifiedVisitId); Assert.Equal(AiOfficeBookingStatus.Confirmed, booking.Status);
        var branch = new Branch { Id = Guid.NewGuid(), Name = "Other reception", IsActive = true }; db.Branches.Add(branch);
        var wrongBranch = new LeadVisit { Id = Guid.NewGuid(), LeadId = s.Lead, BranchId = branch.Id, ReceptionUserId = s.Admin, MeetingOutcome = 1, VisitDateTime = EgyptTime.ToUtc(seed.Arrival), CreatedAt = DateTime.UtcNow };
        db.LeadVisits.Add(wrongBranch); await db.SaveChangesAsync(); await service.AttachVisitLockedAsync(wrongBranch, default); Assert.Null(booking.VerifiedVisitId);
        var matching = new LeadVisit { Id = Guid.NewGuid(), LeadId = s.Lead, BranchId = seed.Policy.BranchId, ReceptionUserId = s.Admin, MeetingOutcome = 1, VisitDateTime = EgyptTime.ToUtc(seed.Arrival), CreatedAt = DateTime.UtcNow };
        db.LeadVisits.Add(matching); await db.SaveChangesAsync();
        await using (var tx = await db.Database.BeginTransactionAsync()) { await service.AttachVisitLockedAsync(matching, default); await db.SaveChangesAsync(); await tx.CommitAsync(); }
        Assert.Equal(matching.Id, booking.VerifiedVisitId); Assert.Equal(AiOfficeBookingStatus.Visited, booking.Status);
    }
    [Fact]
    public async Task Concurrent_duplicate_bookings_commit_one_booking_and_one_reminder()
    {
        var s = await SeedAsync(); var seed = await OfficeSeedAsync(s);
        async Task<bool> Book(string key) { await using var db = fixture.Create(); try { await Office(db, s).SaveAsync(s.Chat, null, s.Admin, Request(seed), key, default); return true; } catch (AiSalesConflict) { return false; } }
        var result = await Task.WhenAll(Book("race-book-one"), Book("race-book-two")); Assert.Single(result, x => x);
        await using var verify = fixture.Create(); Assert.Single(await verify.AiOfficeBookings.Where(b => b.ConversationId == s.Chat).ToListAsync());
        Assert.Single(await verify.FollowUpReminders.Where(r => r.LeadId == s.Lead).ToListAsync());
    }
    [Fact]
    public async Task Human_outcome_cannot_silently_replace_an_active_AI_booking()
    {
        var s = await SeedAsync(); var seed = await OfficeSeedAsync(s); await using var db = fixture.Create();
        await Office(db, s).SaveAsync(s.Chat, null, s.Admin, Request(seed), "protected-office", default);
        var error = await new CustomerOutcomeService(db).ApplyAsync((await db.WhatsAppConversations.FindAsync(s.Chat))!, CustomerOutcomeService.Booked, seed.Arrival.AddDays(1), null, s.Admin, "Admin");
        Assert.NotNull(error); Assert.Equal(seed.Arrival, (await db.Leads.FindAsync(s.Lead))!.AppointmentDate);
    }
    [Fact]
    public async Task Office_http_requires_auth_csrf_visibility_and_admin_policy_approval()
    {
        var s = await SeedAsync(); var seed = await OfficeSeedAsync(s); await using var app = await StartHttpAsync(s); using var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ai-office-bookings/status")).StatusCode);
        await Authenticate(client, s.Admin, "1"); client.DefaultRequestHeaders.Remove("RequestVerificationToken");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/ai-office-bookings/conversations/{s.Chat}", Request(seed))).StatusCode);
        var other = await SeedAsync(); await Authenticate(client, other.Human, "7");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/ai-office-bookings/conversations/{s.Chat}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/ai-office-bookings/policies", new { })).StatusCode);
    }

    [Fact]
    public async Task Actual_reception_check_in_preserves_human_workflow_and_deduplicates_visits()
    {
        var s = await SeedAsync(); await using var db = fixture.Create();
        var receptionist = (await db.Users.FindAsync(s.Human))!; receptionist.Role = 2; await db.SaveChangesAsync();
        var context = new DefaultHttpContext(); context.User = new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, s.Human.ToString()), new Claim(ClaimTypes.Name, "Reception"), new Claim(ClaimTypes.Role, "2") }, "test"));
        var controller = new ReceptionController(db) { ControllerContext = new ControllerContext { HttpContext = context }, TempData = new TempDataDictionary(context, Mock.Of<ITempDataProvider>()) };
        var dto = new WalkInCheckInDto { LeadId = s.Lead };
        await controller.CheckIn(dto, Office(db, s)); await controller.CheckIn(dto, Office(db, s));
        Assert.Equal((byte)6, (await db.Leads.FindAsync(s.Lead))!.Status); Assert.Single(await db.LeadVisits.Where(v => v.LeadId == s.Lead).ToListAsync());
        Assert.Equal(s.Human, (await db.Leads.FindAsync(s.Lead))!.AssignedSalesId); Assert.False(await db.AiOfficeBookings.AnyAsync(b => b.LeadId == s.Lead));
    }
    [Fact]
    public async Task Reservation_report_separates_AI_from_the_human_reviewer_and_keeps_attendance_unknown()
    {
        var s = await SeedAsync(); var seed = await OfficeSeedAsync(s); await using var db = fixture.Create();
        (await db.Users.FindAsync(s.Human))!.Role = 8; await db.SaveChangesAsync();
        var booking = await Office(db, s).SaveAsync(s.Chat, null, s.Human, Request(seed), "report-office", default);
        var context = new DefaultHttpContext(); context.User = new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, s.Admin.ToString()), new Claim(ClaimTypes.Role, "1") }, "test"));
        var controller = new ReservationsController(db, Control(db, s)) { ControllerContext = new ControllerContext { HttpContext = context } };
        await controller.Index(EgyptTime.Today, EgyptTime.Today, null);
        var humanRows = (List<ReservationsController.AgentRow>)controller.ViewData["Rows"]!;
        Assert.Equal(0, humanRows.Single(r => r.Id == s.Human).Total);
        var aiRows = (List<ReservationsController.AiBookingRow>)controller.ViewData["AiBookings"]!;
        Assert.Equal(booking.Id, Assert.Single(aiRows).Id); Assert.Null(aiRows[0].VerifiedVisitId);
    }

    [Fact]
    public async Task Open_visits_allow_two_customers_at_the_same_branch_and_approximate_time()
    {
        var s = await SeedAsync(); var seed = await OfficeSeedAsync(s); var other = (await SeedAsync()) with { Tenant = s.Tenant };
        await using (var setup = fixture.Create())
        {
            var agent = (await setup.AiSalesAgents.FindAsync(other.Agent))!; agent.TenantId = s.Tenant; agent.WhatsAppAccountId = seed.Policy.WhatsAppAccountId;
            (await setup.WhatsAppConversations.FindAsync(other.Chat))!.WhatsAppAccountId = seed.Policy.WhatsAppAccountId; await setup.SaveChangesAsync();
        }
        await EngineSeedAsync(other); var confirmation = await ConfirmationAsync(other);
        await using var db = fixture.Create(); await Office(db, s).SaveAsync(s.Chat, null, s.Admin, Request(seed), "open-first", default);
        await using var otherDb = fixture.Create(); await Office(otherDb, other).SaveAsync(other.Chat, null, other.Admin, Request(seed) with { ConfirmationMessageId = confirmation }, "open-second", default);
        Assert.Equal(2, await db.AiOfficeBookings.CountAsync(b => b.TenantId == s.Tenant && b.BranchId == seed.Policy.BranchId && b.ArrivalLocal == seed.Arrival));
    }
    [Fact]
    public async Task Only_staff_reviewed_voice_can_provide_booking_confirmation_evidence()
    {
        var s = await SeedAsync(); var seed = await OfficeSeedAsync(s); var ids = await AudioSeedAsync(s, 1);
        await using (var invalid = fixture.Create()) await Assert.ThrowsAsync<AiSalesValidation>(() => Office(invalid, s).SaveAsync(s.Chat, null, s.Admin,
            Request(seed) with { ConfirmationMessageId = ids[0] }, "unreviewed-office", default));
        await using var db = fixture.Create(); var voice = Voice(db, s, VoiceMedia().Object, VoiceProvider("ايوه موافق على الاستشارة العامة").Object);
        var turn = Assert.Single(await voice.QueueAsync(s.Chat, s.Admin, 1, ids, default)); await DueAsync(db, turn); await voice.RunNextAsync(default);
        await voice.ReviewAsync(turn.Id, s.Admin, turn.Version, "ايوه موافق على الاستشارة العامة", true, default);
        var booking = await Office(db, s).SaveAsync(s.Chat, null, s.Admin, Request(seed) with { ConfirmationMessageId = ids[0] }, "reviewed-office", default);
        Assert.Equal(turn.Id, booking.ConfirmationVoiceTurnId); Assert.Equal(ids[0], booking.ConfirmationMessageId);
    }
}
