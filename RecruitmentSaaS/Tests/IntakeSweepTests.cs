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
}
