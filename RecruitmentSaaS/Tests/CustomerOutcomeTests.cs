using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using Xunit;

namespace RecruitmentSaaS.Tests;

public sealed partial class FoundationTests
{
    [Theory]
    [InlineData(CustomerOutcomeService.FollowUp)]
    [InlineData(CustomerOutcomeService.Waiting)]
    public async Task Booked_customer_can_switch_to_followup_or_waiting_and_keep_the_change_after_reload(string outcome)
    {
        var s = await SeedAsync();
        await using (var db = fixture.Create())
        {
            var service = new CustomerOutcomeService(db);
            var chat = (await db.WhatsAppConversations.FindAsync(s.Chat))!;
            Assert.Null(await service.ApplyAsync(chat, CustomerOutcomeService.Booked, EgyptTime.Today.AddDays(2).AddHours(12), null, s.Admin, "Admin"));
            var unrelated = new FollowUpReminder { Id = Guid.NewGuid(), LeadId = s.Lead, AssignedToId = s.Human,
                CreatedById = s.Admin, ReminderDate = DateOnly.FromDateTime(EgyptTime.Today.AddDays(1)),
                Notes = "Call about documents", Status = 1, CreatedAt = DateTime.UtcNow };
            db.FollowUpReminders.Add(unrelated); await db.SaveChangesAsync();
            Assert.Null(await service.ApplyAsync(chat, outcome, outcome == CustomerOutcomeService.FollowUp ? DateTime.UtcNow.AddDays(1) : null,
                null, s.Admin, "Admin"));
            Assert.Equal((byte)1, unrelated.Status);
        }

        await using var verify = fixture.Create();
        var savedChat = await verify.WhatsAppConversations.Include(c => c.Lead).SingleAsync(c => c.Id == s.Chat);
        Assert.Equal(outcome, CustomerOutcomeService.Current(savedChat, savedChat.Lead));
        Assert.Equal((byte)4, savedChat.Lead!.Status);
        Assert.Null(savedChat.Lead.AppointmentDate);
        var bookingReminder = await verify.FollowUpReminders.SingleAsync(r => r.LeadId == s.Lead && r.Notes!.StartsWith("تذكير: موعد "));
        Assert.Equal((byte)3, bookingReminder.Status);
        Assert.Equal(s.Admin, bookingReminder.DismissedById);
        Assert.True(await verify.LeadFunnelHistories.AnyAsync(h => h.LeadId == s.Lead && h.FromStatus == 5 && h.ToStatus == 4));
        var followups = await verify.ConversationFollowUps.Where(f => f.ConversationId == s.Chat && f.Status == (byte)FollowUpStatus.Pending).ToListAsync();
        Assert.Equal(outcome == CustomerOutcomeService.FollowUp ? 1 : 0, followups.Count);
    }

    [Fact]
    public async Task Invalid_followup_does_not_remove_the_existing_booking()
    {
        var s = await SeedAsync(); await using var db = fixture.Create();
        var service = new CustomerOutcomeService(db);
        var chat = (await db.WhatsAppConversations.FindAsync(s.Chat))!;
        var appointment = EgyptTime.Today.AddDays(2).AddHours(12);
        Assert.Null(await service.ApplyAsync(chat, CustomerOutcomeService.Booked, appointment, null, s.Admin, "Admin"));
        Assert.NotNull(await service.ApplyAsync(chat, CustomerOutcomeService.FollowUp, null, null, s.Admin, "Admin"));
        await using var verify = fixture.Create();
        var lead = (await verify.Leads.FindAsync(s.Lead))!;
        Assert.Equal((byte)5, lead.Status);
        Assert.Equal(appointment, lead.AppointmentDate);
        Assert.Equal((byte)1, (await verify.FollowUpReminders.SingleAsync(r => r.LeadId == s.Lead)).Status);
    }
}
