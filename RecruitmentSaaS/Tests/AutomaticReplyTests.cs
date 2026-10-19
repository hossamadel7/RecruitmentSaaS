using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using Xunit;

namespace RecruitmentSaaS.Tests;

public sealed partial class FoundationTests
{
    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 1)]
    public async Task Automatic_assignment_starts_assisted_control_without_legacy_intake(bool enabled, byte controller)
    {
        var s = await SeedAsync(collecting: true);
        await using var db = fixture.Create();
        var settings = new AiSalesEngineOptions { AutoReplyEnabled = enabled, AutoReplyAnyAssignedConversation = true,
            AutoReplyAgentIds = [s.Agent], AutoReplySinceUtc = DateTime.UtcNow.AddMinutes(-1) };
        var control = new AiSalesControlService(db, Options.Create(new AiSalesOptions { Enabled = true, TenantId = s.Tenant }), Options.Create(settings));
        await control.AssignAsync(s.Lead, s.Chat, s.Agent, s.Admin, 0, "Explicit assignment", "assign-auto-mode", default);
        Assert.Equal(controller, (byte)(await db.ConversationControls.SingleAsync(c => c.ConversationId == s.Chat)).Controller);
        Assert.False(await db.AiOutboundIntents.AnyAsync(i => i.ConversationId == s.Chat));
    }

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, true, true)]
    public async Task Automatic_replies_are_scoped_idempotent_and_record_no_human_review(bool unknown, bool excluded, bool anyAssigned, bool oldAssignment)
    {
        var s = await SeedAsync(); var fact = await EngineSeedAsync(s);
        await using var db = fixture.Create();
        var message = await db.WhatsAppMessages.SingleAsync(m => m.ConversationId == s.Chat);
        message.CreatedAt = DateTime.UtcNow.AddSeconds(-10); message.WhatsAppTimestamp = message.CreatedAt;
        if (oldAssignment) (await db.AiLeadAssignments.SingleAsync(a => a.ConversationId == s.Chat)).StartedAt = DateTime.UtcNow.AddMinutes(-5);
        await db.SaveChangesAsync();
        var settings = new AiSalesEngineOptions { DraftsEnabled = true, ExternalProcessingApproved = true,
            AutoReplyEnabled = true, AutoReplySinceUtc = DateTime.UtcNow.AddMinutes(-1),
            AutoReplyAnyAssignedConversation = anyAssigned,
            AutoReplyAgentIds = [excluded ? Guid.NewGuid() : s.Agent], ApprovedConversationIds = [anyAssigned ? Guid.NewGuid() : s.Chat] };
        var provider = GoodProvider(fact.Id);
        var drafts = new AiSalesDraftService(db, Control(db, s), new(db), provider.Object, Options.Create(settings));
        var release = new AiSalesDispatchOptions { Enabled = true, ReleaseApproved = true, AutomaticEnabled = true,
            ApprovedConversationIds = anyAssigned ? [] : [s.Chat], ApprovedAccountIds = [message.WhatsAppAccountId] };
        var transport = AcceptedTransport();
        if (unknown) transport.Setup(t => t.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Synthetic uncertain outcome"));
        var dispatch = new AiSalesDispatchService(db, Control(db, s), drafts, transport.Object, Mock.Of<IInboxRealtimeNotifier>(), Options.Create(release));
        var automatic = new AiAutomaticReplyService(db, Control(db, s), drafts, dispatch, Options.Create(settings), Options.Create(release));
        await automatic.RunConversationAsync(s.Chat, default);
        await automatic.RunConversationAsync(s.Chat, default);
        if (excluded || oldAssignment)
        {
            Assert.Empty(await db.AiSalesDrafts.Where(d => d.ConversationId == s.Chat).ToListAsync()); provider.VerifyNoOtherCalls(); transport.VerifyNoOtherCalls(); return;
        }
        var draft = Assert.Single(await db.AiSalesDrafts.Where(d => d.ConversationId == s.Chat).ToListAsync());
        Assert.Equal(AiDraftStatus.AutomaticallyApproved, draft.Status);
        Assert.Null(draft.RequestedById); Assert.Null(draft.ReviewedById); Assert.Null(draft.ReviewedAt);
        var intent = Assert.Single(await db.AiOutboundIntents.Where(i => i.ConversationId == s.Chat).ToListAsync());
        Assert.Null(intent.RequestedById); Assert.Equal(unknown ? "Unknown" : "Sent", intent.State);
        transport.Verify(t => t.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Staff_takeover_during_automatic_generation_prevents_customer_send()
    {
        var s = await SeedAsync(); var fact = await EngineSeedAsync(s);
        await using var db = fixture.Create();
        var message = await db.WhatsAppMessages.SingleAsync(m => m.ConversationId == s.Chat);
        message.CreatedAt = DateTime.UtcNow.AddSeconds(-10); message.WhatsAppTimestamp = message.CreatedAt; await db.SaveChangesAsync();
        var settings = new AiSalesEngineOptions { DraftsEnabled = true, ExternalProcessingApproved = true, AutoReplyEnabled = true,
            AutoReplySinceUtc = DateTime.UtcNow.AddMinutes(-1), AutoReplyAgentIds = [s.Agent], ApprovedConversationIds = [s.Chat] };
        var provider = GoodProvider(fact.Id);
        provider.Setup(p => p.GenerateAsync(It.IsAny<AiSalesContext>(), It.IsAny<CancellationToken>())).Returns(async () =>
        {
            await using var staff = fixture.Create();
            await Control(staff, s).ControlAsync(s.Chat, s.Admin, 1, "takeover", "Staff takes over", "staff-auto-stop", default);
            return new AiDraftProposal("رد قديم", "answer", "Synthetic", false, [fact.Id], "synthetic");
        });
        var drafts = new AiSalesDraftService(db, Control(db, s), new(db), provider.Object, Options.Create(settings));
        var release = new AiSalesDispatchOptions { Enabled = true, ReleaseApproved = true, AutomaticEnabled = true,
            ApprovedConversationIds = [s.Chat], ApprovedAccountIds = [message.WhatsAppAccountId] };
        var transport = new Mock<IWhatsAppCloudApiService>(MockBehavior.Strict);
        var dispatch = new AiSalesDispatchService(db, Control(db, s), drafts, transport.Object, Mock.Of<IInboxRealtimeNotifier>(), Options.Create(release));
        await new AiAutomaticReplyService(db, Control(db, s), drafts, dispatch, Options.Create(settings), Options.Create(release)).RunConversationAsync(s.Chat, default);
        Assert.Equal(AiDraftStatus.Stale, (await db.AiSalesDrafts.SingleAsync(d => d.ConversationId == s.Chat)).Status);
        Assert.Empty(await db.AiOutboundIntents.Where(i => i.ConversationId == s.Chat).ToListAsync()); transport.VerifyNoOtherCalls();
    }
}
