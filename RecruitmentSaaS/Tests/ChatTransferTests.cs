using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RecruitmentSaaS.Controllers;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.Security.Claims;
using Xunit;

namespace RecruitmentSaaS.Tests;

public sealed partial class FoundationTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Old_handoff_cannot_reclaim_a_chat_or_assign_inactive_staff(bool formerActive, bool transferred)
    {
        var s = await SeedAsync(); await using var db = fixture.Create();
        var former = (await db.Users.FindAsync(s.Human))!;
        former.IsActive = formerActive;
        var target = new User { Id = Guid.NewGuid(), BranchId = former.BranchId, FullName = "New owner",
            Email = Guid.NewGuid() + "@example.test", PasswordHash = "unused", Role = 3, IsActive = true };
        db.Users.Add(target);
        var chat = (await db.WhatsAppConversations.FindAsync(s.Chat))!;
        chat.LeadId = null;
        chat.AssignedSalesAgentId = transferred ? target.Id : null;
        var account = (await db.WhatsAppAccounts.FindAsync(chat.WhatsAppAccountId))!;
        // Also cover a transfer to the account's default owner.
        account.AssignedSalesAgentId = target.Id;
        var handoff = new WhatsAppHandoff { Id = Guid.NewGuid(), LeadId = s.Lead, WhatsAppAccountId = account.Id,
            AssignedSalesAgentId = former.Id, ReferenceCode = "REF-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
            GeneratedWhatsAppUrl = "https://example.test/unused", Status = (byte)HandoffStatus.Sent, CreatedAt = DateTime.UtcNow };
        db.WhatsAppHandoffs.Add(handoff); await db.SaveChangesAsync();
        var processor = new WhatsAppWebhookProcessor(db, Mock.Of<IInboxRealtimeNotifier>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WhatsAppWebhookProcessor>.Instance,
            Mock.Of<IWhatsAppCloudApiService>(), Mock.Of<INotificationService>(), null!, null!, Control(db, s), null!);
        var connect = typeof(WhatsAppWebhookProcessor).GetMethod("TryConnectHandoffAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await (Task)connect.Invoke(processor, new object?[] { chat, account, "201000000000", handoff.ReferenceCode,
            DateTime.UtcNow, CancellationToken.None })!;
        await db.SaveChangesAsync();
        Assert.Equal(transferred ? target.Id : (Guid?)null, chat.AssignedSalesAgentId);
        Assert.Equal(s.Lead, chat.LeadId);
        Assert.Equal((byte)HandoffStatus.Connected, handoff.Status);
    }

    [Fact]
    public async Task Automatic_chat_start_refuses_an_inactive_lead_owner()
    {
        var s = await SeedAsync(); await using var db = fixture.Create();
        (await db.Users.FindAsync(s.Human))!.IsActive = false;
        await db.SaveChangesAsync();
        var starter = new LeadChatStarter(db, Mock.Of<IWhatsAppCloudApiService>(), Mock.Of<IInboxRealtimeNotifier>(),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), Control(db, s));
        var result = await starter.StartAsync((await db.Leads.FindAsync(s.Lead))!, null, null);
        Assert.False(result.Success);
        Assert.False(result.TemplateSent);
        Assert.Empty(await db.WhatsAppMessages.Where(m => m.ConversationId == s.Chat).ToListAsync());
    }

    [Fact]
    public async Task Routing_keeps_current_owner_and_rejects_inactive_historical_owners()
    {
        var s = await SeedAsync(); await using var db = fixture.Create();
        var oldOwner = (await db.Users.FindAsync(s.Human))!;
        var target = new User { Id = Guid.NewGuid(), BranchId = oldOwner.BranchId, FullName = "New owner",
            Email = Guid.NewGuid() + "@example.test", PasswordHash = "unused", Role = 3, IsActive = true };
        db.Users.Add(target); await db.SaveChangesAsync();
        // Even a still-active former lead/handoff owner cannot reclaim a transferred chat.
        Assert.Equal(target.Id, await ChatAssignmentPolicy.ResolveAsync(db, target.Id, oldOwner.Id));
        oldOwner.IsActive = false; await db.SaveChangesAsync();
        Assert.Null(await ChatAssignmentPolicy.ResolveAsync(db, null, oldOwner.Id));
        Assert.Null(await ChatAssignmentPolicy.ResolveAsync(db, oldOwner.Id, oldOwner.Id));
        Assert.Equal(target.Id, await ChatAssignmentPolicy.ResolveAsync(db, oldOwner.Id, target.Id));
    }

    [Fact]
    public async Task Transfer_moves_converted_leads_pending_links_account_defaults_and_release_snapshots()
    {
        var s = await SeedAsync(); await using var db = fixture.Create();
        var former = (await db.Users.FindAsync(s.Human))!;
        var target = new User { Id = Guid.NewGuid(), BranchId = former.BranchId, FullName = "New owner",
            Email = Guid.NewGuid() + "@example.test", PasswordHash = "unused", Role = 3, IsActive = true };
        db.Users.Add(target);
        var lead = (await db.Leads.FindAsync(s.Lead))!;
        var chat = (await db.WhatsAppConversations.FindAsync(s.Chat))!;
        var account = (await db.WhatsAppAccounts.FindAsync(chat.WhatsAppAccountId))!;
        account.AssignedSalesAgentId = former.Id;
        var handoff = new WhatsAppHandoff { Id = Guid.NewGuid(), LeadId = lead.Id, WhatsAppAccountId = account.Id,
            AssignedSalesAgentId = former.Id, ReferenceCode = "REF-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(),
            GeneratedWhatsAppUrl = "https://example.test/unused", Status = (byte)HandoffStatus.Sent, CreatedAt = DateTime.UtcNow };
        db.WhatsAppHandoffs.Add(handoff); await db.SaveChangesAsync();
        // A separate AI-controlled chat remembers the former salesperson for eventual release.
        var aiContact = new WhatsAppContact { Id = Guid.NewGuid(), Name = "Other chat", WhatsAppPhoneNumber = Guid.NewGuid().ToString("N") };
        var aiChat = new WhatsAppConversation { Id = Guid.NewGuid(), Contact = aiContact,
            WhatsAppAccountId = account.Id, LeadId = lead.Id, AssignedSalesAgentId = former.Id };
        db.WhatsAppConversations.Add(aiChat); await db.SaveChangesAsync();
        await Control(db, s).AssignAsync(s.Lead, aiChat.Id, s.Agent, s.Admin, 0, "Prepare", "transfer-snapshot", default,
            conversationOnly: true);
        lead.IsConverted = true; await db.SaveChangesAsync();

        using var services = new ServiceCollection().AddMemoryCache()
            .AddSingleton(Mock.Of<IInboxRealtimeNotifier>()).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services,
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, s.Admin.ToString()),
                new Claim(ClaimTypes.Name, "Admin") }, "test")) };
        var controller = new AdminController(db, Mock.Of<IWebHostEnvironment>(), Mock.Of<INotificationService>())
        {
            Url = Mock.Of<IUrlHelper>(),
            ControllerContext = new ControllerContext { HttpContext = http },
            TempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>())
        };
        await controller.ToggleUser(former.Id, target.Id);
        Assert.False(former.IsActive);
        Assert.Equal(target.Id, chat.AssignedSalesAgentId);
        Assert.Equal(target.Id, lead.AssignedSalesId);
        Assert.Equal(target.Id, account.AssignedSalesAgentId);
        Assert.Equal(target.Id, handoff.AssignedSalesAgentId);
        var snapshot = await db.AiLeadAssignments.SingleAsync(a => a.ConversationId == aiChat.Id && a.EndedAt == null);
        Assert.Equal(target.Id, snapshot.PreviousConversationSalesId);
        await Control(db, s).ControlAsync(aiChat.Id, s.Admin, 1, "release", "Return to staff", "transfer-release", default);
        Assert.Equal(target.Id, aiChat.AssignedSalesAgentId);
    }
}
