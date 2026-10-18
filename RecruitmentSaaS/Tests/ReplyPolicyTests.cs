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
    private static AiSalesContext Followup(string question, AiContextFact[]? facts = null) => new(
        new(null, null, null, null, null, "", 0),
        [new(Guid.NewGuid(), "customer", "Text", "حابب أجي السبت", DateTime.UtcNow.AddMinutes(-2)),
         new(Guid.NewGuid(), "staff_or_script", "Text", "الرسوم 35000 جنيه في مصر", DateTime.UtcNow.AddMinutes(-1)),
         new(Guid.NewGuid(), "customer", "Text", question, DateTime.UtcNow)], facts ?? []);

    [Theory]
    [InlineData("هفكر")]
    [InlineData("هفكر وأرد عليك")]
    [InlineData("محتاج وقت أفكر")]
    public void Thinking_gets_short_acknowledgement_without_offer_or_invitation(string text)
    {
        var reply = AiSalesReplyPolicy.Create(Followup(text))!;
        Assert.Equal("acknowledgement", reply.Intent);
        Assert.False(reply.NeedsHuman);
        Assert.Empty(reply.SourceIds);
        Assert.Contains("خد وقتك", reply.Reply);
        Assert.DoesNotContain("المكتب", reply.Reply);
        Assert.DoesNotContain("650", reply.Reply);
    }

    [Theory]
    [InlineData("هفكر بس السكن عليكم؟")]
    [InlineData("مش غالي، بس هدفع امتى؟")]
    [InlineData("المرتب كام؟")]
    public void Mixed_questions_are_not_swallowed_by_acknowledgement(string text)
        => Assert.Null(AiSalesReplyPolicy.Create(Followup(text)));

    [Theory]
    [InlineData(0, false, true)]
    [InlineData(1, false, false)]
    [InlineData(0, true, false)]
    public void Price_objection_uses_only_current_fact_discount_policy(int kind, bool expired, bool supported)
    {
        var fact = new AiContextFact(Guid.NewGuid(), 1, (AiKnowledgeKind)kind, "رسوم",
            "الخصم يمكن طلبه من الإدارة، ولا تؤكد خصمًا قبل اعتماده.", DateTime.UtcNow.AddDays(expired ? -1 : 1));
        var reply = AiSalesReplyPolicy.Create(Followup("بس 35000 كتير أوي", [fact]))!;
        Assert.False(reply.NeedsHuman);
        Assert.DoesNotContain("650", reply.Reply);
        Assert.DoesNotContain("السبت", reply.Reply);
        Assert.Equal(supported ? new[] { fact.Id } : [], reply.SourceIds);
        Assert.Equal(supported, reply.Reply.Contains("الخصم مش مؤكد"));
    }

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
