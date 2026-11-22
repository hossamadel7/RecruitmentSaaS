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
    public void Arabizi_booking_request_is_closed_by_the_agent_not_routed_to_staff()
        => Assert.Null(AiSalesReplyPolicy.Create(Chat("Ya3ni lw hatmashoha 120 ana hagy bokra ahgz", language: "arabizi")));

    [Theory]
    [InlineData("Khlass mmkn agy yom el etnen el gyy mmkn agy el sa3a kam", "arabizi")]
    [InlineData("ممكن أجي يوم الاتنين الساعة كام؟", "ar-EG")]
    public void A_question_about_when_to_come_is_answered_by_the_model_with_office_hours(string text, string language)
        => Assert.Null(AiSalesReplyPolicy.Create(Chat(text, language: language)));

    [Fact]
    public void Visit_question_is_removed_right_after_another_invitation()
    {
        var reply = "فاهمة يا فندم، المبلغ كبير فعلاً. تكلفة عقود عمان 125 ألف جنيه. تحب نحدد يوم للزيارة؟";
        var polished = AiSalesReplyPolish.Polish(reply, WithAi("Bas 125 alf dah kteer awyy", "تحب تيجي يوم إيه؟"));
        Assert.DoesNotContain("الزيارة", polished);
        Assert.Contains("125 ألف", polished);
        // One invitation is fine when the agent has not just invited.
        Assert.Equal(reply, AiSalesReplyPolish.Polish(reply, WithAi("Bas 125 alf dah kteer awyy", "تكلفة العقد 125 ألف.")));
    }

    [Fact]
    public void Someone_will_contact_you_never_reaches_the_customer()
        => Assert.Equal("فيه فرص كتير في عمان يا فندم.",
            AiSalesReplyPolish.Polish("فيه فرص كتير في عمان يا فندم. هيتواصل مع حضرتك مستشار من فريقنا قريب. خليك مطمئن!", WithAi("متاح عقود", "اهلا")));

    [Fact]
    public void A_named_visit_day_can_be_confirmed()
    {
        var reply = "تمام يا فندم، مستنيك يوم الاتنين إن شاء الله.";
        Assert.Equal(reply, AiSalesReplyPolish.Polish(reply, WithAi("هاجي يوم الاتنين", "تحب تيجي يوم إيه؟")));
    }

    [Fact]
    public void Visit_question_is_kept_when_the_customer_asks_about_coming()
    {
        var reply = "المكتب شغال من 10 الصبح لـ 6 المسا من السبت للخميس. تحب تيجي يوم إيه؟";
        Assert.Equal(reply, AiSalesReplyPolish.Polish(reply, Chat("ممكن أجي امتى؟")));
    }

    [Fact]
    public void Office_welcome_push_is_removed_right_after_another_invitation()
    {
        var polished = AiSalesReplyPolish.Polish(
            "رواتب صنايعية المعمار في عمان من 312 ل 364 ريال عماني. لو محتاج تفاصيل أكتر، هنتكلم في المقابلة، وأهلاً بيك في المكتب قريب إن شاء الله!",
            WithAi("Oly el tafaseell", "تحب تيجي يوم إيه؟"));
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
        => Assert.Equal("تحب نحدد يوم للزيارة؟", AiSalesReplyPolish.Polish("تحب نحدد يوم للزيارة؟", WithAi("تمام", "تحب تيجي يوم إيه؟")));

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

    private static AiContextFact Office() => new(Guid.NewGuid(), 1, AiKnowledgeKind.Fact, "عنوان المكتب والمواعيد والزيارة",
        "عنوان المكتب: 103 مكرر شارع الهرم. الموقع: https://maps.google.com/?q=29.991724,31.154583 . المواعيد من 10 صباحًا إلى 6 مساءً.", DateTime.UtcNow.AddDays(30));

    private static AiSalesContext WithAi(string customer, string aiEarlier, params AiContextFact[] facts) => new(new(null, null, null, null, null, "", 0),
        [new(Guid.NewGuid(), "customer", "Text", "اهلا", DateTime.UtcNow.AddMinutes(-3)),
         new(Guid.NewGuid(), "ai", "Text", aiEarlier, DateTime.UtcNow.AddMinutes(-2)),
         new(Guid.NewGuid(), "customer", "Text", customer, DateTime.UtcNow)], facts, "ar-EG");

    [Theory]
    [InlineData("ممكن لوكيسن")]
    [InlineData("ابعتلي اللوكيشن")]
    [InlineData("location")]
    public void Location_request_gets_the_approved_map_link(string text)
    {
        var office = Office();
        var reply = AiSalesReplyPolicy.Create(WithAi(text, "المكتب مفتوح من السبت للخميس.", office))!;
        Assert.Contains("https://maps.google.com/?q=29.991724,31.154583", reply.Reply);
        Assert.Equal("answer", reply.Intent); Assert.False(reply.NeedsHuman); Assert.Equal([office.Id], reply.SourceIds);
    }

    [Theory]
    [InlineData("شكرا. وكل. الاحترام", "العفو يا فندم 🙏")]
    [InlineData("تسلم", "العفو يا فندم 🙏")]
    [InlineData("تمام", "تمام يا فندم 👍")]
    [InlineData("تمام إن شاء الله", "تمام يا فندم 👍")]
    public void Bare_thanks_or_ok_gets_a_short_acknowledgement(string text, string expected)
        => Assert.Equal(expected, AiSalesReplyPolicy.Create(WithAi(text, "المكتب مفتوح من السبت للخميس."))!.Reply);

    [Fact]
    public void Ok_after_our_question_is_left_to_the_model()
        => Assert.Null(AiSalesReplyPolicy.Create(WithAi("تمام", "ابن عمك عنده جواز سفر؟")));

    [Fact]
    public void Closers_and_promises_are_removed()
    {
        var polished = AiSalesReplyPolish.Polish("فيه عقود في عمان وأرمينيا. هبعتلك التفاصيل قريب. لا يهمك! نورتنا!", WithAi("متاح عقود عمل", "اهلا"));
        Assert.Equal("فيه عقود في عمان وأرمينيا.", polished);
    }

    [Fact]
    public void Visit_welcome_closer_is_removed()
        => Assert.Equal("مسؤول التعاقدات هيشرحلك كل حاجة بالتفصيل.",
            AiSalesReplyPolish.Polish("مسؤول التعاقدات هيشرحلك كل حاجة بالتفصيل. نورتنا في الزيارة!", WithAi("ممكن اعرف التفاصيل", "اهلا")));

    [Fact]
    public void Hours_already_given_are_not_repeated_unless_asked_again()
    {
        var earlier = "المكتب مفتوح من السبت للخميس من 10 صباحًا لحد 6 مساءً.";
        var reply = "مطلوب جواز سفر وصورة شخصية. المكتب مفتوح من السبت للخميس من 10 صباحًا ل6 مساءً.";
        Assert.Equal("مطلوب جواز سفر وصورة شخصية.", AiSalesReplyPolish.Polish(reply, WithAi("ايه الورق المطلوب", earlier)));
        Assert.Equal(reply, AiSalesReplyPolish.Polish(reply, WithAi("المكتب مفتوح امتى والورق ايه", earlier)));
    }

    [Fact]
    public void Agent_introduces_itself_once_when_taking_over()
    {
        var proposal = new AiDraftProposal("أهلاً بحضرتك، فيه عقود في عمان.", "answer", "", false, [], "m");
        var first = AiSalesReplyPolish.Introduce(proposal, Chat("متاح عقود عمل", previousReply: "شكراً يا فندم هيتواصل مع حضرتك مستشار"));
        Assert.Equal("أهلاً يا فندم، معاك جيهان من شركة الفهد العربي، وأنا اللي هكمل مع حضرتك.\nفيه عقود في عمان.", first.Reply);
        Assert.Same(proposal, AiSalesReplyPolish.Introduce(proposal, WithAi("متاح عقود عمل", "فيه عقود")));
    }

    [Theory]
    [InlineData("تركيا — الوظائف المتاحة والسن والخبرة", "ممكن اعرف تفصيل اكتر بخصوص العمل في الخارج", true)]
    [InlineData("أرمينيا — الوظائف والرواتب", "متاح عقود عمل", true)]
    [InlineData("السعودية — دليفري كيتا وهنجرستيشن", "عنوان المكتب", false)]
    public void Job_lists_are_pinned_when_the_customer_asks_about_work(string title, string customer, bool pinned)
        => Assert.Equal(pinned, AiSalesContextService.Pinned(title, customer));

    [Fact]
    public void Prompt_covers_eligibility_job_salaries_cheaper_offers_and_retry_corrections()
    {
        var p = OpenAiSalesDraftProvider.Instructions;
        Assert.Contains("ELIGIBILITY", p);
        Assert.Contains("quote a salary only for the job the FACT gives it for", p);
        Assert.Contains("approved offer with a lower cost", p);
        Assert.Contains("ConversationState.Correction", p);
        Assert.Contains("Never invent working hours", p);
        Assert.Contains("JOB MATCH", p);
        Assert.Contains("The initial payment for procedures is not the contract", p);
        Assert.DoesNotContain("لو احتجت توضيح أي نقطة أنا معاك", p);
        Assert.Contains("FACT IDs", AiSalesDraftService.CorrectionFor("MissingEvidence"));
    }
}
