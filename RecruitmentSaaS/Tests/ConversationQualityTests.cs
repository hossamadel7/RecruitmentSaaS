using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using Xunit;

namespace RecruitmentSaaS.Tests;

/// <summary>Regressions from the 22 Nov 2026 owner test chat: silence, visit push, name repetition, Arabizi.</summary>
public sealed class ConversationQualityTests
{
    private static AiSalesContext Chat(string customer, string? previousReply = null, string language = "ar-EG")
    {
        var messages = new List<AiContextMessage>();
        if (previousReply != null)
        {
            messages.Add(new(Guid.NewGuid(), "customer", "Text", "اهلا", DateTime.UtcNow.AddMinutes(-3)));
            messages.Add(new(Guid.NewGuid(), "staff_or_script", "Text", previousReply, DateTime.UtcNow.AddMinutes(-2)));
        }
        messages.Add(new(Guid.NewGuid(), "customer", "Text", customer, DateTime.UtcNow));
        return new(new(null, null, null, null, null, "", 0), messages.ToArray(), [], language);
    }

    [Theory]
    [InlineData("3ndi 50", 50)]
    [InlineData("50 b2olk", 50)]
    [InlineData("50 sada2ny", 50)]
    [InlineData("عندي 50", 50)]
    [InlineData("35", 35)]
    public void Arabizi_letters_written_as_digits_are_not_read_as_part_of_the_age(string said, int age)
        => Assert.Equal(age, IntakeScript.ParseAge(said));

    [Fact]
    public void Arabizi_booking_request_goes_to_staff()
    {
        var reply = AiSalesReplyPolicy.Create(Chat("Ya3ni lw hatmashoha 120 ana hagy bokra ahgz", language: "arabizi"))!;
        Assert.Equal("handoff", reply.Intent); Assert.True(reply.NeedsHuman);
        Assert.False(AiSalesDraftService.ClaimsBooking(reply.Reply));
    }

    [Theory]
    [InlineData("Khlass mmkn agy yom el etnen el gyy mmkn agy el sa3a kam", "arabizi")]
    [InlineData("ممكن أجي يوم الاتنين الساعة كام؟", "ar-EG")]
    public void A_question_about_when_to_come_is_answered_by_the_model_with_office_hours(string text, string language)
        => Assert.Null(AiSalesReplyPolicy.Create(Chat(text, language: language)));

    [Fact]
    public void Visit_question_is_removed_when_the_customer_did_not_ask_about_visiting()
    {
        var polished = AiSalesReplyPolish.Polish("فاهمة يا فندم، المبلغ كبير فعلاً. تكلفة عقود عمان 125 ألف جنيه. تحب نحدد يوم للزيارة؟",
            Chat("Bas 125 alf dah kteer awyy"));
        Assert.DoesNotContain("الزيارة", polished);
        Assert.Contains("125 ألف", polished);
    }

    [Fact]
    public void Visit_question_is_kept_when_the_customer_asks_about_coming()
    {
        var reply = "المكتب شغال من 10 الصبح لـ 6 المسا من السبت للخميس. تحب تيجي يوم إيه؟";
        Assert.Equal(reply, AiSalesReplyPolish.Polish(reply, Chat("ممكن أجي امتى؟")));
    }

    [Fact]
    public void Office_welcome_push_is_removed()
    {
        var polished = AiSalesReplyPolish.Polish(
            "رواتب صنايعية المعمار في عمان من 312 ل 364 ريال عماني. لو محتاج تفاصيل أكتر، هنتكلم في المقابلة، وأهلاً بيك في المكتب قريب إن شاء الله!",
            Chat("Oly el tafaseell"));
        Assert.DoesNotContain("المكتب", polished);
        Assert.Contains("312", polished);
    }

    [Fact]
    public void Customer_name_is_not_repeated_in_consecutive_replies_and_stock_closers_are_removed()
    {
        var polished = AiSalesReplyPolish.Polish(
            "تمام يا أحمد، أنا هنا معاك. هتحتاج جواز سفر ساري وصورة شخصية. لو محتاج تفاصيل أكتر، أنا معاك!",
            Chat("eh el wara2 el mehtag", previousReply: "تمام يا أحمد، المرتب من 312 ل 364 ريال."));
        Assert.DoesNotContain("أحمد", polished);
        Assert.DoesNotContain("معاك", polished);
        Assert.Contains("جواز سفر ساري", polished);
    }

    [Fact]
    public void Titled_name_becomes_ya_fandem_and_bel_nesba_opener_is_dropped()
    {
        var polished = AiSalesReplyPolish.Polish("تمام يا حضرة أحمد، بالنسبة لمطلوب منك، هتحتاج جواز سفر ساري وصورة شخصية.", Chat("el wara2 eh"));
        Assert.Equal("تمام يا فندم، هتحتاج جواز سفر ساري وصورة شخصية.", polished);
    }

    [Fact]
    public void Polish_never_empties_a_reply()
        => Assert.Equal("تحب نحدد يوم للزيارة؟", AiSalesReplyPolish.Polish("تحب نحدد يوم للزيارة؟", Chat("تمام")));

    [Theory]
    [InlineData("عنوان المكتب والمواعيد والزيارة", "Oly el tafaseell", true)]
    [InlineData("تركيا — الوظائف المتاحة والسن والخبرة", "Oly el tafaseell", true)]
    [InlineData("تركيا — الرسوم والدفع والتفاصيل غير المحددة", "125 alf dah kteer awyy fi haga arkhas", true)]
    [InlineData("عُمان — الوظائف والرواتب وتكلفة العقد", "125 alf dah kteer awyy", true)]
    [InlineData("تركيا — الرسوم والدفع والتفاصيل غير المحددة", "eh el wara2 el mehtag", false)]
    public void Office_age_and_cost_facts_are_pinned_into_context(string title, string customer, bool pinned)
        => Assert.Equal(pinned, AiSalesContextService.Pinned(title, customer));

    [Theory]
    [InlineData(true, "handoff", true)]
    [InlineData(true, "stop", true)]
    [InlineData(true, "answer", false)]
    [InlineData(false, "answer", true)]
    [InlineData(false, "handoff", false)]
    public void Handoff_and_stop_acknowledgements_can_be_sent_automatically(bool needsHuman, string intent, bool sendable)
        => Assert.Equal(sendable, AiSalesDraftService.AutoSendable(new AiSalesDraft { NeedsHuman = needsHuman, Intent = intent }));

    [Fact]
    public void Prompt_covers_eligibility_job_salaries_cheaper_offers_and_retry_corrections()
    {
        var p = OpenAiSalesDraftProvider.Instructions;
        Assert.Contains("ELIGIBILITY", p);
        Assert.Contains("quote a salary only for the job the FACT gives it for", p);
        Assert.Contains("approved offer with a lower cost", p);
        Assert.Contains("ConversationState.Correction", p);
        Assert.Contains("Never invent working hours", p);
        Assert.DoesNotContain("لو احتجت توضيح أي نقطة أنا معاك", p);
        Assert.Contains("FACT IDs", AiSalesDraftService.CorrectionFor("MissingEvidence"));
    }
}
