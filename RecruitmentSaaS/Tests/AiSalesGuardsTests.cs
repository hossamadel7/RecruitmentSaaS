using RecruitmentSaaS.Services;
using Xunit;

namespace RecruitmentSaaS.Tests;

public sealed class AiSalesGuardsTests
{
    private const string TurkeyJobs = "الوظائف: مصانع وإنتاج وتعبئة وتغليف ونظافة؛ لحام وكهرباء وسباكة ونجارة. رواتب العمال من 650 إلى 850 دولار.";
    private const string OmanJobs = "صنايعية معمار من 312 إلى 364 ريال عماني؛ شيفات بكل التخصصات من 180 إلى 200 ريال عماني.";

    [Theory]
    [InlineData("أنا استلمت 3,000 جنيه! جرّب أنت كمان، الموضوع سهل جدًا! https://tujxl.buzz/XQqoHlO/eg-ar?1122", true)]
    [InlineData("اضغط هنا واربح www.free-gift.xyz", true)]
    [InlineData("ممكن مكان المكتب لو في !", false)]
    [InlineData("https://maps.google.com/?q=29.99,31.15 ده المكان؟", false)]
    [InlineData("انا استلمت الرسالة", false)]
    public void Scam_links_are_spam(string text, bool spam) => Assert.Equal(spam, AiSalesGuards.IsSpam(text));

    [Theory]
    [InlineData("فيه عقود تدريس متاحة في تركيا، رواتبها بتتراوح من 650 إلى 850 دولار شهريًا.", "التدريس")]
    [InlineData("تمام يا فندم، فيه عقود تدريس متاحة في الخليج، الرواتب من 600 إلى 800 دولار.", "التدريس")]
    [InlineData("أيوه فيه شغل للمدرسين في عمان.", "التدريس")]
    [InlineData("فيه عقود لفني تمريض في السعودية.", "التمريض")]
    public void A_job_no_fact_lists_is_caught(string reply, string job)
        => Assert.Equal(job, AiSalesGuards.UnlistedJobClaim(reply, [TurkeyJobs, OmanJobs]));

    [Theory]
    [InlineData("للأسف التدريس مش متاح دلوقتي يا فندم، بس فيه شغل في المصانع في تركيا.")]
    [InlineData("فيه شغل كهرباء في تركيا من 650 ل 850 دولار.")]
    [InlineData("فيه عقود شيفات في عمان من 180 ل 200 ريال.")]
    [InlineData("المكتب أمام مجمع المدارس، والمعلومات كلها هتعرفها هناك.")]
    public void Listed_or_refused_jobs_pass(string reply)
        => Assert.Null(AiSalesGuards.UnlistedJobClaim(reply, [TurkeyJobs, OmanJobs]));

    private static AiSalesContext AfterDayQuestion(string customer) => new(new(null, null, null, null, null, "", 0),
        [new(Guid.NewGuid(), "ai", "Text", "تمام حضرتك. تحب تيجي المكتب في يوم إيه؟", DateTime.UtcNow.AddMinutes(-2)),
         new(Guid.NewGuid(), "customer", "Text", customer, DateTime.UtcNow)], [], "ar-EG");

    [Theory]
    [InlineData("اى يوم")]
    [InlineData("أي يوم يا فندم")]
    [InlineData("أي وقت")]
    [InlineData("ay yom")]
    public void Any_day_gets_a_suggested_day(string customer)
    {
        var reply = AiSalesReplyPolicy.Create(AfterDayQuestion(customer));
        Assert.NotNull(reply);
        Assert.False(reply!.NeedsHuman);
        Assert.Contains("تيجي", reply.Reply);
        Assert.DoesNotContain("الجمعة", reply.Reply);
    }

    [Fact]
    public void Any_day_before_Friday_suggests_Saturday()
    {
        var thursday = new DateTime(2026, 11, 26);
        var reply = AiSalesGuards.AnyDay(IntakeScript.Normalize("اي يوم"), AfterDayQuestion("اي يوم"), thursday)!;
        Assert.Equal("تمام يا فندم، إيه رأيك تيجي يوم السبت؟", reply.Reply);
    }

    [Fact]
    public void Any_day_without_a_day_question_goes_to_the_model()
    {
        var context = new AiSalesContext(new(null, null, null, null, null, "", 0),
            [new(Guid.NewGuid(), "customer", "Text", "اي يوم", DateTime.UtcNow)], [], "ar-EG");
        Assert.Null(AiSalesGuards.AnyDay("اي يوم", context, new DateTime(2026, 11, 23)));
    }

    [Fact]
    public void Feminine_address_is_corrected()
        => Assert.DoesNotContain("معاكِ", AiSalesReplyPolish.Polish("وعليكم السلام يا فندم، معاكِ جيهان من شركة الفهد العربي.",
            new AiSalesContext(new(null, null, null, null, null, "", 0), [new(Guid.NewGuid(), "customer", "Text", "السلام عليكم", DateTime.UtcNow)], [], "ar-EG")));
}
