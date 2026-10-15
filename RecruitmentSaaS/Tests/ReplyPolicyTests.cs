using Microsoft.EntityFrameworkCore;
using Moq;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using Xunit;

namespace RecruitmentSaaS.Tests;

public sealed partial class FoundationTests
{
    [Theory]
    [InlineData("مش مهتم، متبعتليش تاني", "stop")]
    [InlineData("مش مهتم وعايز أكلم موظف", "stop")]
    [InlineData("عايز أكلم موظف", "handoff")]
    [InlineData("هجيلكم الجمعة الساعة 10", "handoff")]
    [InlineData("عندكم السعودية؟", "handoff")]
    [InlineData("متاح شغل سواق في تركيا؟", "handoff")]
    [InlineData("أنا سوداني ينفع؟", "handoff")]
    public async Task Restricted_requests_bypass_provider_and_remain_review_drafts_without_CRM_actions(string question, string intent)
    {
        var (s, _) = await TurkeySeedAsync(question); await using var db = fixture.Create();
        var provider = new Mock<IAiSalesDraftProvider>(MockBehavior.Strict);
        var draft = await Engine(db, s, provider.Object).GenerateAsync(s.Chat, s.Admin, 1, "routing:" + s.Chat, default);
        Assert.Equal(AiDraftStatus.ReviewRequired, draft.Status); Assert.Equal(intent, draft.Intent);
        Assert.True(draft.NeedsHuman); Assert.Equal("local-policy", draft.Model);
        Assert.DoesNotContain("أهلاً", draft.Reply);
        if (question.Contains("الجمعة")) Assert.Contains("مغلق يوم الجمعة", draft.Reply);
        provider.VerifyNoOtherCalls();
        Assert.False(await db.WhatsAppMessages.AnyAsync(m => m.ConversationId == s.Chat && m.Direction == (byte)MessageDirection.Outgoing));
        Assert.False(await db.AiOutboundIntents.AnyAsync(i => i.ConversationId == s.Chat));
        Assert.False(await db.AiOfficeBookings.AnyAsync(b => b.ConversationId == s.Chat));
        Assert.Null((await db.Leads.FindAsync(s.Lead))!.AppointmentDate);
    }
}

public sealed class ReplyPolicyTests
{
    [Theory]
    [InlineData(0, false, "المكتب مفتوح الخميس، والجمعة مغلق.", true)]
    [InlineData(1, false, "المكتب مفتوح الخميس، والجمعة مغلق.", false)]
    [InlineData(0, true, "المكتب مفتوح الخميس، والجمعة مغلق.", false)]
    [InlineData(0, false, "المكتب ليس مغلق يوم الجمعة.", false)]
    [InlineData(0, false, "المكتب يوم الجمعة غير مغلق.", false)]
    public void Friday_closure_requires_explicit_unexpired_fact_and_otherwise_requests_review(int kind, bool expired, string content, bool supported)
    {
        var fact = new AiContextFact(Guid.NewGuid(), 1, (AiKnowledgeKind)kind, "المكتب", content, DateTime.UtcNow.AddDays(expired ? -1 : 1));
        var context = new AiSalesContext(new(null, null, null, null, null, "", 0),
            [new(Guid.NewGuid(), "customer", "Text", "هجيلكم الجمعة", DateTime.UtcNow)], [fact]);
        var reply = AiSalesReplyPolicy.Create(context)!;
        Assert.True(reply.NeedsHuman); Assert.Equal("handoff", reply.Intent);
        Assert.Equal(supported, reply.Reply.Contains("مغلق يوم الجمعة"));
        Assert.Equal(supported ? new[] { fact.Id } : [], reply.SourceIds);
    }

    [Fact]
    public void Prior_customer_or_staff_text_does_not_trigger_local_policy_for_new_salary_question()
    {
        var context = new AiSalesContext(new(null, null, null, null, null, "", 0),
            [new(Guid.NewGuid(), "customer", "Text", "مش مهتم", DateTime.UtcNow.AddMinutes(-2)),
             new(Guid.NewGuid(), "staff_or_script", "Text", "المكتب مغلق يوم الجمعة", DateTime.UtcNow.AddMinutes(-1)),
             new(Guid.NewGuid(), "customer", "Text", "المرتب كام؟", DateTime.UtcNow)], []);
        Assert.Null(AiSalesReplyPolicy.Create(context));
        Assert.Null(AiSalesReplyPolicy.Create(context with { RecentMessages = context.RecentMessages[..2] }));
    }
}
