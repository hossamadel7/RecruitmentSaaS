using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using Xunit;

namespace RecruitmentSaaS.Tests;

public sealed partial class FoundationTests
{
    [Fact]
    public async Task Intake_timeout_completes_an_AI_owned_chat_without_a_second_owner_and_other_chats_still_complete()
    {
        var ai = await SeedAsync(collecting: true); var normal = await SeedAsync(collecting: true);
        await using (var db = fixture.Create())
        {
            if (!await db.LeadFormSettings.AnyAsync()) db.LeadFormSettings.Add(new LeadFormSetting { Id = Guid.NewGuid() });
            var quiet = DateTime.UtcNow.AddDays(-2);
            // The chat is the AI agent's, but the lead it matches was never marked as the AI's.
            var aiChat = (await db.WhatsAppConversations.FindAsync(ai.Chat))!;
            aiChat.AssignedSalesAgentId = null; aiChat.AssignedAiSalesAgentId = ai.Agent;
            aiChat.IntakeAge = 30; aiChat.IntakeStartedAt = aiChat.IntakeLastCustomerAt = quiet;
            var normalChat = (await db.WhatsAppConversations.FindAsync(normal.Chat))!;
            normalChat.IntakeAge = 50; normalChat.IntakeStartedAt = normalChat.IntakeLastCustomerAt = quiet;
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.Create())
        {
            var intake = new AiIntakeService(db, Mock.Of<IWhatsAppCloudApiService>(), Mock.Of<IInboxRealtimeNotifier>(),
                Mock.Of<INotificationService>(), NullLogger<AiIntakeService>.Instance, Control(db, ai));
            await intake.SweepTimeoutsAsync(default);
        }

        await using var verify = fixture.Create();
        var done = (await verify.WhatsAppConversations.FindAsync(ai.Chat))!;
        Assert.Equal((byte)IntakeStatus.Completed, done.IntakeStatus);
        Assert.Equal(ai.Agent, done.AssignedAiSalesAgentId); Assert.Null(done.AssignedSalesAgentId);
        var other = (await verify.WhatsAppConversations.FindAsync(normal.Chat))!;
        Assert.Equal((byte)IntakeStatus.Completed, other.IntakeStatus);
        Assert.Equal(normal.Human, other.AssignedSalesAgentId);
    }

    [Fact]
    public async Task Intake_timeout_without_age_sends_a_country_ad_chat_to_that_countrys_rotation()
    {
        var s = await SeedAsync();
        await using (var db = fixture.Create())
        {
            (await db.CountryRoutingRules.SingleAsync(r => r.Country == "تركيا")).Enabled = true;
            db.LeadRotationMembers.Add(new LeadRotationMember { ScopeKey = CountryRouting.Scope("تركيا"), UserId = s.Human, Position = 0, Share = 1 });
            await db.SaveChangesAsync();
        }
        var chatId = await NoAgeChatAsync("إعلان تركيا — لحق مكان و ابعتلنا رساله دلوقتي");

        await SweepAsync(s);

        await using var verify = fixture.Create();
        var chat = (await verify.WhatsAppConversations.FindAsync(chatId))!;
        Assert.Equal((byte)IntakeStatus.Completed, chat.IntakeStatus);
        Assert.Equal(s.Human, chat.AssignedSalesAgentId);
        Assert.Null(chat.PendingTeamManagerId);
        Assert.Contains("دولة الإعلان", chat.IntakeHandoffReason);
        var lead = (await verify.Leads.FindAsync(chat.LeadId))!;
        Assert.Equal("تركيا", lead.AdCountry); Assert.Equal("تركيا", lead.RoutedCountry);
        Assert.Null(lead.Age); Assert.Equal(s.Human, lead.AssignedSalesId);
    }

    [Fact]
    public async Task Intake_timeout_without_age_still_goes_to_the_team_leader_without_a_country_rotation()
    {
        var s = await SeedAsync();
        await using (var db = fixture.Create())
        {
            (await db.CountryRoutingRules.SingleAsync(r => r.Country == "عمان")).Enabled = true; // on, but nobody takes its leads
            await db.SaveChangesAsync();
        }
        var emptyRotation = await NoAgeChatAsync("وظائف سلطنة عمان");
        var noAd = await NoAgeChatAsync(null);

        await SweepAsync(s);

        await using var verify = fixture.Create();
        foreach (var id in new[] { emptyRotation, noAd })
        {
            var chat = (await verify.WhatsAppConversations.FindAsync(id))!;
            Assert.Equal((byte)IntakeStatus.HandedOff, chat.IntakeStatus);
            Assert.Null(chat.AssignedSalesAgentId); Assert.Null(chat.LeadId);
            Assert.Equal("العميل ما ردش على السن", chat.IntakeHandoffReason);
        }
    }

    private async Task SweepAsync(Seed s)
    {
        await using var db = fixture.Create();
        if (!await db.LeadFormSettings.AnyAsync()) { db.LeadFormSettings.Add(new LeadFormSetting { Id = Guid.NewGuid() }); await db.SaveChangesAsync(); }
        var intake = new AiIntakeService(db, Mock.Of<IWhatsAppCloudApiService>(), Mock.Of<IInboxRealtimeNotifier>(),
            Mock.Of<INotificationService>(), NullLogger<AiIntakeService>.Instance, Control(db, s));
        await intake.SweepTimeoutsAsync(default);
    }

    /// <summary>A new chat on its own number where the customer wrote once (from an ad when given) and never gave the age.</summary>
    private async Task<Guid> NoAgeChatAsync(string? adHeadline)
    {
        await using var db = fixture.Create();
        var quiet = DateTime.UtcNow.AddDays(-3); // past the 48-hour wait and the 24-hour window, so nothing is sent
        var account = new WhatsAppAccount { Id = Guid.NewGuid(), Name = "Ad number", PhoneNumberId = Guid.NewGuid().ToString("N"), WabaId = "test",
            DisplayPhoneNumber = "2015" + Random.Shared.Next(10_000_000, 99_999_999), IsActive = true };
        var contact = new WhatsAppContact { Id = Guid.NewGuid(), Name = "Ad customer", WhatsAppPhoneNumber = "2011" + Random.Shared.Next(10_000_000, 99_999_999) };
        var chat = new WhatsAppConversation { Id = Guid.NewGuid(), WhatsAppAccount = account, Contact = contact,
            IntakeStatus = (byte)IntakeStatus.Collecting, IntakeStartedAt = quiet, IntakeLastCustomerAt = quiet };
        db.WhatsAppConversations.Add(chat);
        db.WhatsAppMessages.Add(new WhatsAppMessage { Id = Guid.NewGuid(), ConversationId = chat.Id, WhatsAppAccountId = account.Id,
            Direction = (byte)MessageDirection.Incoming, MessageType = (byte)WhatsAppMessageType.Text, MessageSource = (byte)MessageSource.CloudApi,
            Status = (byte)MessageStatus.Received, TextBody = "مرحبًا", WhatsAppTimestamp = quiet, CreatedAt = quiet,
            ReferralSourceId = adHeadline == null ? null : "ad-" + Guid.NewGuid().ToString("N"), ReferralHeadline = adHeadline });
        await db.SaveChangesAsync();
        return chat.Id;
    }
}
