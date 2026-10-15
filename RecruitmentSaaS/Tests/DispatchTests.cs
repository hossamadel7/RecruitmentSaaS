using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using Microsoft.AspNetCore.TestHost;
using System.Net;
using System.Net.Http.Json;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using Xunit;

namespace RecruitmentSaaS.Tests;

public sealed partial class FoundationTests
{
    [Fact]
    public async Task Reviewed_sending_requires_exact_conversation_scope_and_rechecks_it_before_transport()
    {
        var (s, draft, account) = await DispatchSeedAsync(); await using var db = fixture.Create();
        var transport = new Mock<IWhatsAppCloudApiService>(MockBehavior.Strict);
        var settings = new AiSalesDispatchOptions { Enabled = true, ReleaseApproved = true, ApprovedAccountIds = [account], ApprovedConversationIds = [Guid.NewGuid()] };
        await Assert.ThrowsAsync<AiSalesValidation>(() => Dispatcher(db, s, account, transport.Object, settings).QueueAsync(draft.Id, s.Admin, 1, "not-in-scope", default));
        Assert.False(await db.AiOutboundIntents.AnyAsync(i => i.DraftId == draft.Id));
        settings.ApprovedConversationIds = [s.Chat];
        var intent = await Dispatcher(db, s, account, transport.Object, settings).QueueAsync(draft.Id, s.Admin, 1, "in-scope", default);
        settings.ApprovedConversationIds = [];
        await Assert.ThrowsAsync<AiSalesValidation>(() => Dispatcher(db, s, account, transport.Object, settings).DispatchAsync(intent.Id, s.Admin, default));
        transport.VerifyNoOtherCalls();
        Assert.False(await db.WhatsAppMessages.AnyAsync(m => m.ConversationId == s.Chat && m.Direction == 2));
    }
    private async Task<(Seed Seed, AiSalesDraft Draft, Guid Account)> DispatchSeedAsync()
    {
        var s = await SeedAsync(); var fact = await EngineSeedAsync(s);
        await using var db = fixture.Create(); var engine = Engine(db, s, GoodProvider(fact.Id).Object);
        var draft = await engine.GenerateAsync(s.Chat, s.Admin, 1, Guid.NewGuid().ToString(), default);
        await engine.ReviewAsync(draft.Id, s.Admin, true, default);
        return (s, draft, await db.WhatsAppConversations.Where(c => c.Id == s.Chat).Select(c => c.WhatsAppAccountId).SingleAsync());
    }
    private static AiSalesDispatchService Dispatcher(RecruitmentCrmContext db, Seed s, Guid account,
        IWhatsAppCloudApiService api, AiSalesDispatchOptions? options = null) => new(db, Control(db, s),
        Engine(db, s, new Mock<IAiSalesDraftProvider>(MockBehavior.Strict).Object), api,
        new Mock<IInboxRealtimeNotifier>().Object, Options.Create(options ?? new() { Enabled = true, ReleaseApproved = true, ApprovedAccountIds = [account], ApprovedConversationIds = [s.Chat] }));
    private static Mock<IWhatsAppCloudApiService> AcceptedTransport()
    {
        var api = new Mock<IWhatsAppCloudApiService>(MockBehavior.Strict);
        api.Setup(a => a.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WhatsAppSendResult { Success = true, WhatsAppMessageId = "wamid.synthetic." + Guid.NewGuid() });
        return api;
    }
    [Fact]
    public async Task Dispatch_is_idempotent_and_records_virtual_author_without_human_impersonation()
    {
        var (s, d, account) = await DispatchSeedAsync(); await using var db = fixture.Create(); var api = AcceptedTransport();
        var service = Dispatcher(db, s, account, api.Object);
        var intent = await service.QueueAsync(d.Id, s.Admin, 1, "queue", default);
        Assert.Equal(intent.Id, (await service.QueueAsync(d.Id, s.Admin, 1, "queue", default)).Id);
        Assert.False(await db.WhatsAppMessages.AnyAsync(m => m.ConversationId == s.Chat && m.Direction == 2));
        Assert.Equal("Sent", (await service.DispatchAsync(intent.Id, s.Admin, default)).State);
        Assert.Equal("Sent", (await service.DispatchAsync(intent.Id, s.Admin, default)).State);
        var message = await db.WhatsAppMessages.SingleAsync(m => m.ConversationId == s.Chat && m.Direction == 2);
        Assert.Equal(s.Agent, message.SenderAiSalesAgentId); Assert.Null(message.SenderUserId); Assert.Equal(d.Reply, message.TextBody);
        api.Verify(a => a.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), d.Reply, It.IsAny<CancellationToken>()), Times.Once);
        await Assert.ThrowsAsync<AiSalesConflict>(() => service.QueueAsync(d.Id, s.Admin, 1, "another", default));
    }
    [Theory]
    [InlineData(false, true, true)] [InlineData(true, false, true)] [InlineData(true, true, false)]
    public async Task All_release_gates_fail_closed(bool enabled, bool approved, bool accountAllowed)
    {
        var (s, d, account) = await DispatchSeedAsync(); await using var db = fixture.Create(); var api = new Mock<IWhatsAppCloudApiService>(MockBehavior.Strict);
        var service = Dispatcher(db, s, account, api.Object, new() { Enabled = enabled, ReleaseApproved = approved, ApprovedAccountIds = accountAllowed ? [account] : [], ApprovedConversationIds = [s.Chat] });
        await Assert.ThrowsAsync<AiSalesValidation>(() => service.QueueAsync(d.Id, s.Admin, 1, "disabled", default));
        Assert.False(await db.AiOutboundIntents.AnyAsync(i => i.ConversationId == s.Chat)); api.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task Takeover_invalidates_queued_dispatch_and_does_not_call_transport()
    {
        var (s, d, account) = await DispatchSeedAsync(); await using var db = fixture.Create(); var api = new Mock<IWhatsAppCloudApiService>(MockBehavior.Strict);
        var service = Dispatcher(db, s, account, api.Object); var intent = await service.QueueAsync(d.Id, s.Admin, 1, "takeover", default);
        await Control(db, s).ControlAsync(s.Chat, s.Admin, 1, "takeover", "Staff handling", "dispatch-takeover", default);
        Assert.Equal("Invalidated", (await service.DispatchAsync(intent.Id, s.Admin, default)).State); api.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task Changed_context_is_discarded_before_transport()
    {
        var (s, d, account) = await DispatchSeedAsync(); await using var db = fixture.Create(); var api = new Mock<IWhatsAppCloudApiService>(MockBehavior.Strict);
        var service = Dispatcher(db, s, account, api.Object); var intent = await service.QueueAsync(d.Id, s.Admin, 1, "stale", default);
        await db.AiSalesKnowledge.Where(k => k.TenantId == s.Tenant).ExecuteUpdateAsync(u => u.SetProperty(k => k.Status, AiKnowledgeStatus.Retired));
        Assert.Equal("Invalidated", (await service.DispatchAsync(intent.Id, s.Admin, default)).State); api.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task Closed_reply_window_blocks_even_an_already_queued_reply()
    {
        var (s, d, account) = await DispatchSeedAsync(); await using var db = fixture.Create(); var api = new Mock<IWhatsAppCloudApiService>(MockBehavior.Strict);
        var service = Dispatcher(db, s, account, api.Object); var intent = await service.QueueAsync(d.Id, s.Admin, 1, "window", default);
        await db.WhatsAppMessages.Where(m => m.ConversationId == s.Chat).ExecuteUpdateAsync(u => u.SetProperty(m => m.WhatsAppTimestamp, DateTime.UtcNow.AddHours(-25)));
        // Timestamp changes can also invalidate context; either path must prevent a send.
        try { await service.DispatchAsync(intent.Id, s.Admin, default); } catch (AiSalesValidation) { }
        api.VerifyNoOtherCalls(); Assert.False(await db.WhatsAppMessages.AnyAsync(m => m.ConversationId == s.Chat && m.Direction == 2));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Uncertain_result_is_durable_and_never_retried(bool throws)
    {
        var (s, d, account) = await DispatchSeedAsync(); await using var db = fixture.Create(); var api = new Mock<IWhatsAppCloudApiService>(MockBehavior.Strict);
        var setup = api.Setup(a => a.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()));
        if (throws) setup.ThrowsAsync(new HttpRequestException("private transport body"));
        else setup.ReturnsAsync(new WhatsAppSendResult { Success = true }); // acceptance without a durable wamid is ambiguous
        var service = Dispatcher(db, s, account, api.Object); var intent = await service.QueueAsync(d.Id, s.Admin, 1, "unknown", default);
        var result = await service.DispatchAsync(intent.Id, s.Admin, default); Assert.Equal("Unknown", result.State);
        Assert.NotNull(result.AttemptedAt); Assert.Equal("TransportOutcomeUnknown", result.ErrorCode);
        Assert.Equal("Unknown", (await service.DispatchAsync(intent.Id, s.Admin, default)).State);
        await Assert.ThrowsAsync<AiSalesConflict>(() => service.CancelAsync(intent.Id, s.Admin, default));
        api.Verify(a => a.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
    [Fact]
    public async Task Restarted_attempt_becomes_unknown_without_retry()
    {
        var (s, d, account) = await DispatchSeedAsync(); await using var db = fixture.Create(); var api = new Mock<IWhatsAppCloudApiService>(MockBehavior.Strict);
        var service = Dispatcher(db, s, account, api.Object); var intent = await service.QueueAsync(d.Id, s.Admin, 1, "crash", default);
        await db.AiOutboundIntents.Where(i => i.Id == intent.Id).ExecuteUpdateAsync(u => u.SetProperty(i => i.State, "Sending").SetProperty(i => i.AttemptedAt, DateTime.UtcNow));
        Assert.Equal("Unknown", (await service.DispatchAsync(intent.Id, s.Admin, default)).State); api.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task Cancellation_works_with_release_disabled_and_foreign_tenant_cannot_dispatch()
    {
        var (s, d, account) = await DispatchSeedAsync(); await using var db = fixture.Create(); var api = new Mock<IWhatsAppCloudApiService>(MockBehavior.Strict);
        var intent = await Dispatcher(db, s, account, api.Object).QueueAsync(d.Id, s.Admin, 1, "cancel", default);
        var other = await SeedAsync();
        await Assert.ThrowsAsync<AiSalesValidation>(() => Dispatcher(db, other, account, api.Object).DispatchAsync(intent.Id, other.Admin, default));
        Assert.Equal("Cancelled", (await Dispatcher(db, s, account, api.Object, new()).CancelAsync(intent.Id, s.Admin, default)).State); api.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Concurrent_send_requests_make_one_transport_attempt()
    {
        var (s, d, account) = await DispatchSeedAsync(); var api = AcceptedTransport(); Guid id;
        await using (var db = fixture.Create()) id = (await Dispatcher(db, s, account, api.Object).QueueAsync(d.Id, s.Admin, 1, "parallel", default)).Id;
        async Task<string> Send()
        {
            await using var db = fixture.Create(); return (await Dispatcher(db, s, account, api.Object).DispatchAsync(id, s.Admin, default)).State;
        }
        Assert.All(await Task.WhenAll(Send(), Send()), state => Assert.Equal("Sent", state));
        api.Verify(a => a.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }
    [Fact]
    public async Task Budget_is_reserved_before_transport_and_counts_unknown_attempts()
    {
        var (s, d, account) = await DispatchSeedAsync(); await using var db = fixture.Create(); var api = new Mock<IWhatsAppCloudApiService>(MockBehavior.Strict);
        var service = Dispatcher(db, s, account, api.Object, new() { Enabled = true, ReleaseApproved = true, ApprovedAccountIds = [account], ApprovedConversationIds = [s.Chat], MaxAttemptsPerDay = 1 });
        var intent = await service.QueueAsync(d.Id, s.Admin, 1, "budget", default);
        db.AiOutboundIntents.Add(new() { TenantId = s.Tenant, ConversationId = s.Chat, OperationKey = "prior", State = "Unknown", AttemptedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(); await Assert.ThrowsAsync<AiSalesConflict>(() => service.DispatchAsync(intent.Id, s.Admin, default));
        Assert.False(await db.WhatsAppMessages.AnyAsync(m => m.ConversationId == s.Chat && m.Direction == 2)); api.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task Unreviewed_or_escalated_reply_and_foreign_draft_cannot_be_queued()
    {
        var (s, d, account) = await DispatchSeedAsync(); await using var db = fixture.Create(); var api = new Mock<IWhatsAppCloudApiService>(MockBehavior.Strict);
        var service = Dispatcher(db, s, account, api.Object);
        await db.AiSalesDrafts.Where(x => x.Id == d.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, AiDraftStatus.ReviewRequired));
        await Assert.ThrowsAsync<AiSalesConflict>(() => service.QueueAsync(d.Id, s.Admin, 1, "unreviewed", default));
        await db.AiSalesDrafts.Where(x => x.Id == d.Id).ExecuteUpdateAsync(u => u.SetProperty(x => x.Status, AiDraftStatus.Reviewed).SetProperty(x => x.NeedsHuman, true));
        await Assert.ThrowsAsync<AiSalesConflict>(() => service.QueueAsync(d.Id, s.Admin, 1, "escalated", default));
        var other = await SeedAsync();
        await Assert.ThrowsAsync<AiSalesValidation>(() => Dispatcher(db, other, account, api.Object).QueueAsync(d.Id, other.Admin, 1, "foreign", default)); api.VerifyNoOtherCalls();
    }
    [Fact]
    public async Task Dispatch_api_enforces_admin_csrf_and_disabled_release()
    {
        var (s, d, _) = await DispatchSeedAsync(); await using var app = await StartHttpAsync(s); using var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ai-sales-dispatch/status")).StatusCode);
        await Authenticate(client, s.Admin, "7");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ai-sales-dispatch/status")).StatusCode);
        await Authenticate(client, s.Admin, "1");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ai-sales-dispatch/status")).StatusCode);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "http-dispatch");
        var body = new { draftId = d.Id, expectedOwnershipVersion = 1 };
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsJsonAsync("/api/ai-sales-dispatch", body)).StatusCode);
        client.DefaultRequestHeaders.Remove("RequestVerificationToken");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/ai-sales-dispatch", body)).StatusCode);
    }
}
