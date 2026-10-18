using Microsoft.EntityFrameworkCore;
using Moq;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using Xunit;

namespace RecruitmentSaaS.Tests;

public sealed partial class FoundationTests
{
    [Theory]
    [InlineData(4, false)]
    [InlineData(2, true)]
    public async Task Uninterpreted_voice_can_request_text_after_review_while_other_media_keeps_handoff(byte messageType, bool handoff)
    {
        var s = await SeedAsync();
        await EngineSeedAsync(s);
        var ids = await AudioSeedAsync(s, 1);
        await using var db = fixture.Create();
        var message = await db.WhatsAppMessages.SingleAsync(m => m.Id == ids[0]);
        message.MessageType = messageType;
        await db.SaveChangesAsync();
        var provider = new Mock<IAiSalesDraftProvider>(MockBehavior.Strict);
        var engine = Engine(db, s, provider.Object);
        var draft = await engine.GenerateAsync(s.Chat, s.Admin, 1, "media-clarification", default);
        Assert.Equal(AiDraftStatus.ReviewRequired, draft.Status);
        Assert.Equal(handoff, draft.NeedsHuman);
        Assert.Equal(handoff ? "handoff" : "acknowledgement", draft.Intent);
        if (!handoff) Assert.Equal("ممكن توضح طلبك بالكتابة عشان نقدر نساعدك بدقة؟", draft.Reply);
        provider.VerifyNoOtherCalls();
        var account = message.WhatsAppAccountId;
        var transport = AcceptedTransport();
        var dispatch = Dispatcher(db, s, account, transport.Object);
        await Assert.ThrowsAsync<AiSalesConflict>(() => dispatch.QueueAsync(draft.Id, s.Admin, 1, "before-review", default));
        await engine.ReviewAsync(draft.Id, s.Admin, true, default);
        if (handoff)
            await Assert.ThrowsAsync<AiSalesConflict>(() => dispatch.QueueAsync(draft.Id, s.Admin, 1, "after-review", default));
        else
        {
            var intent = await dispatch.QueueAsync(draft.Id, s.Admin, 1, "after-review", default);
            Assert.Equal("Sent", (await dispatch.DispatchAsync(intent.Id, s.Admin, default)).State);
        }
    }

    [Fact]
    public async Task Social_acknowledgement_needs_no_business_claim_and_still_requires_review_before_sending()
    {
        var (s, _) = await TurkeySeedAsync("هفكر");
        await using var db = fixture.Create();
        var provider = new Mock<IAiSalesDraftProvider>();
        provider.Setup(p => p.GenerateAsync(It.IsAny<AiSalesContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AiDraftProposal("تمام يا فندم، خد وقتك. لو احتجت توضيح أنا معاك.",
                "acknowledgement", "Customer requests time; no business claims.", false, [], "synthetic-model"));
        var engine = Engine(db, s, provider.Object);
        var draft = await engine.GenerateAsync(s.Chat, s.Admin, 1, "thinking", default);
        Assert.Equal(AiDraftStatus.ReviewRequired, draft.Status);
        Assert.False(draft.NeedsHuman);
        Assert.Equal("[]", draft.SourceIdsJson);
        var account = await db.WhatsAppConversations.Where(c => c.Id == s.Chat).Select(c => c.WhatsAppAccountId).SingleAsync();
        var transport = AcceptedTransport();
        var dispatch = Dispatcher(db, s, account, transport.Object);
        await Assert.ThrowsAsync<AiSalesConflict>(() => dispatch.QueueAsync(draft.Id, s.Admin, 1, "before-review", default));
        transport.VerifyNoOtherCalls();
        await engine.ReviewAsync(draft.Id, s.Admin, true, default);
        var intent = await dispatch.QueueAsync(draft.Id, s.Admin, 1, "after-review", default);
        Assert.Equal("Sent", (await dispatch.DispatchAsync(intent.Id, s.Admin, default)).State);
    }
}
