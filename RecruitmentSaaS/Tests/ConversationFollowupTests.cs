using Microsoft.EntityFrameworkCore;
using Moq;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using Xunit;

namespace RecruitmentSaaS.Tests;

public sealed partial class FoundationTests
{
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
