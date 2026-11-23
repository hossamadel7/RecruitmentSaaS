using RecruitmentSaaS.Models.Entities;
using System.Text.RegularExpressions;

namespace RecruitmentSaaS.Services;

/// <summary>Conservative draft routing, not a booking or an employment decision.</summary>
public static class AiSalesReplyPolicy
{
    public static AiDraftProposal? Create(AiSalesContext context)
    {
        var latest = context.RecentMessages.LastOrDefault();
        var language = context.Language ?? "ar-EG";
        if (language == "ar-EG") return CreateEgyptian(context);
        if (latest?.Speaker != "customer" || latest.Type is not ("Text" or "TranscribedAudio")) return null;
        var text = IntakeScript.Normalize(latest.Text);
        if (IntakeScript.NotInterested(text) || Regex.IsMatch(text, @"\b(?:unsubscribe|not interested|do not contact|stop messaging|stop contacting)\b|لا اريد التواصل|لا ارغب|mesh mehtam|msh mehtam"))
            return Local(AiSalesLanguage.SafeLocal("stop", language), "stop", "Customer contact refusal; staff stop workflow required.");
        if (IntakeScript.WantsPerson(text) || Regex.IsMatch(text, @"\b(?:speak to (?:a |an )?(?:human|person|agent)|talk to (?:a |an )?(?:human|person|agent)|human agent)\b|موظف بشري|التحدث الي موظف|akallem (?:7ad|had|mowazaf)"))
            return Local(AiSalesLanguage.SafeLocal("handoff", language), "handoff", "Customer requested staff; no further intake required.");
        if (Regex.IsMatch(text.Trim(), @"^(?:i(?:'ll| will) think (?:about it)?|let me think|hafakar|hafakkar|سافكر|سافكر في الامر)[.! ]*$"))
            return new(AiSalesLanguage.SafeLocal("thinking", language), "acknowledgement", "Customer needs time; no pressure.", false, [], "local-policy");
        if (language == "arabizi" && (Location(text, context) ?? Thanks(text, context) ?? AiSalesGuards.AnyDay(text, context, EgyptTime.Today)) is { } local) return local;
        return null; // Business replies must be generated in the customer's language from current facts.
    }
    private static AiDraftProposal? CreateEgyptian(AiSalesContext context)
    {
        var latest = context.RecentMessages.LastOrDefault();
        if (latest?.Speaker != "customer" || latest.Type is not ("Text" or "TranscribedAudio")) return null;
        var text = IntakeScript.Normalize(latest.Text);
        // A refusal takes precedence even if the same message also asks for a person.
        if (IntakeScript.NotInterested(text))
            return Local("حاضر يا فندم، نحترم رغبتك في عدم المتابعة.", "stop", "Customer declined contact; staff must handle the stop request.");
        if (IntakeScript.WantsPerson(text))
            return Local("حاضر يا فندم، طلبك محتاج متابعة من موظف.", "handoff", "Customer requested a person; do not condition handoff on another sales question.");

        // Bounded conversational acts: do not let a generic sales pitch override a request for time.
        // A mixed question (e.g. "هفكر بس السكن عليكم؟") must still reach the provider.
        var plain = Regex.Replace(text, @"[^\p{L}\p{N}\s]", " ");
        plain = Regex.Replace(plain, @"\s+", " ").Trim();
        if (Regex.IsMatch(plain, @"^(?:طيب |تمام |انا )?(?:هفكر|حافكر|هفكر شويه|هفكر وارد عليك|هفكر و ارد عليك|محتاج وقت افكر|محتاج وقت|خليني افكر)(?: يا فندم)?$"))
            return new("تمام يا فندم خد وقتك براحتك، وأنا معاك في أي وقت 🤝",
                "acknowledgement", "Customer requests time; no sales pitch or contact-stop action.", false, [], "local-policy");

        var previousReply = context.RecentMessages.LastOrDefault(m => m.Speaker != "customer")?.Text ?? "";
        var feeContext = Has(text, "رسوم", "تكلف", "35000", "٣٥٠٠٠") || Has(IntakeScript.Normalize(previousReply), "رسوم", "35000", "٣٥٠٠٠");
        var priceConcern = Has(text, "غالي", "كتير", "كثير", "مبلغ كبير", "مش معايا المبلغ");
        var questionOrNegation = Has(text, "مش غالي", "مش كتير", "مش كثير", "مش كبير", "كام", "امتي", "ليه", "ازاي", "هل", "؟", "?");
        if (feeContext && priceConcern && !questionOrNegation)
        {
            var discountPolicy = context.Knowledge.FirstOrDefault(k => k.Kind == AiKnowledgeKind.Fact && k.ValidUntil > DateTime.UtcNow &&
                IntakeScript.Normalize(k.Content).Contains(IntakeScript.Normalize("الخصم يمكن طلبه من الإدارة، ولا تؤكد خصمًا قبل اعتماده")));
            return discountPolicy == null
                ? new("فاهمة يا فندم إن المبلغ كبير بالنسبة لحضرتك. لو تحب، نراجع استفسارك عن الرسوم مع المسؤول.",
                    "acknowledgement", "Price objection acknowledged; no discount policy established and no action claimed.", false, [], "local-policy")
                : new("فاهمة يا فندم إن المبلغ كبير بالنسبة لحضرتك. ممكن تطلب من الإدارة مراجعة إمكانية خصم، لكن الخصم مش مؤكد قبل موافقتهم.",
                    "answer", "Price objection; approved policy allows requesting a discount review, never guarantees one.", false, [discountPolicy.Id], "local-policy");
        }

        // Named-day requests require staff/calendar review. Free text is not a confirmed appointment.
        if (text.Contains("الجمعه") && Has(text, "هجيل", "اجي", "ازياره", "ازور", "زياره", "موعد", "معاد", "المكتب"))
        {
            // Only these explicit current Fact formulations support stating Friday closure.
            // No inference from Playbook text, customer history, or the default booking schedule.
            var fact = context.Knowledge.FirstOrDefault(k => k.Kind == AiKnowledgeKind.Fact && k.ValidUntil > DateTime.UtcNow &&
                IntakeScript.Normalize(k.Content).Contains("المكتب") &&
                Has(IntakeScript.Normalize(k.Content), "والجمعه مغلق", "ومغلق يوم الجمعه"));
            return fact == null ? null
                : new("المكتب مغلق يوم الجمعة يا فندم، تقدر تيجي أي يوم من السبت للخميس.", "answer",
                    "Approved Fact states Friday closure; no appointment created.", false, [fact.Id], "local-policy");
        }
        if ((Location(text, context) ?? Thanks(text, context) ?? AiSalesGuards.AnyDay(text, context, EgyptTime.Today)) is { } local) return local;
        // This is an information handoff, never rejection of the applicant or a hiring decision.
        if (text.Contains("السعوديه") && !context.Knowledge.Any(k => k.Kind == AiKnowledgeKind.Fact && k.ValidUntil > DateTime.UtcNow &&
            Has(IntakeScript.Normalize(k.Title + " " + k.Content), "السعوديه", "سعودي", "saudi")))
            return Local("استفسار حضرتك عن السعودية محتاج مراجعة من مسؤول الشركة قبل ما نأكد تفاصيل أي عرض.", "handoff", "Unsupported market request; do not infer that the company has no other-country vacancies.");
        // Driver and "ينفع" questions are answered by the agent itself (positively, without inventing a salary).
        return null;
    }

    // Arabic and Arabizi wording for booking, coming over, a named day/time, and a question.
    private static readonly string[] BookWords = ["احجزلي", "احجز لي", "احجزيلي", "عايز احجز", "عاوز احجز", "ممكن احجز", "احجز ميعاد", "احجز معاد",
        "احجز موعد", "هاجي احجز", "هجي احجز", "اجي احجز", "ahgz", "a7gz", "ahgez", "a7gez", "ahgaz", "e7gezly", "ehgezly", "e7gzly", "ehgzly", "7gzly", "hgzly"];
    private static readonly string[] ComeWords = ["اجي", "هجي", "هاجي", "جاي", "agy", "aji", "hagy", "hagi", "haagy", "ageelko", "agelko", "gay "];
    private static readonly string[] DayWords = ["بكره", "النهارده", "السبت", "الحد", "الاحد", "الاتنين", "الاثنين", "التلات", "الثلاثاء", "الاربع", "الخميس",
        "bokra", "bukra", "boukra", "naharda", "el sabt", "el had", "el etnen", "el itnen", "el tanat", "el talat", "el arba3", "el khamis", "el 5amis"];
    private static readonly string[] QuestionWords = ["كام", "امتي", "ازاي", "ينفع", "؟", "?", " kam", "emta", "imta", "ezay", "ezzay", "yenfa3"];

    /// <summary>A booking request, or a stated visit day/time, is never booked from chat text: staff confirm it in the CRM.
    /// A question about timing ("اجي الساعه كام؟") is left to the model so it can answer with the office hours.</summary>
    public static AiDraftProposal? Booking(string text)
    {
        var timed = Regex.IsMatch(text, @"(?:الساعه|sa3a|sa3)\s*\d") || Has(text, DayWords);
        if (!Has(text, BookWords) && !(Has(text, ComeWords) && timed && !Has(text, QuestionWords))) return null;
        return Local("تمام يا فندم، هبلغ الموظف المسؤول يراجع طلب حضرتك ويأكد معاك الميعاد.", "handoff",
            "Customer asked to book or named a visit day/time; staff must confirm in the CRM. No appointment created.");
    }

    private static readonly string[] LocationWords = ["لوكيشن", "لوكيسن", "لوكشن", "لوكيشان", "اللوكيشن", "الموقع", "موقع المكتب", "خريطه", "الخريطه",
        "location", "lokeshn", "lokation", "loc", "map", "maps", "pin"];
    private static readonly System.Text.RegularExpressions.Regex MapLink = new(@"https://(?:maps\.google\.com|goo\.gl/maps|maps\.app\.goo\.gl)/\S+",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>A location request gets the approved map link, never a model paraphrase.</summary>
    public static AiDraftProposal? Location(string text, AiSalesContext context)
    {
        if (text.Length > 60 || !LocationWords.Any(w => Regex.IsMatch(text, @"(?<!\p{L})" + Regex.Escape(w) + @"(?!\p{L})"))) return null;
        var fact = context.Knowledge.FirstOrDefault(k => k.Kind == AiKnowledgeKind.Fact && k.ValidUntil > DateTime.UtcNow && MapLink.IsMatch(k.Content));
        if (fact == null) return null;
        var link = MapLink.Match(fact.Content).Value.TrimEnd('.', '،', ',', ')');
        return new("اتفضل لوكيشن المكتب يا فندم 📍\n" + link, "answer", "Location request answered with the approved map link.", false, [fact.Id], "local-policy");
    }

    private static readonly string[] ThanksWords = ["شكرا", "متشكر", "تسلم", "تسلمي", "الله يسلمك", "ربنا يكرمك", "مشكور", "جزاك الله", "shokran", "tslam", "teslam", "thanks", "thank you"];
    private static readonly string[] OkWords = ["تمام", "ماشي", "اوك", "اوكي", "حاضر", "طيب", "ok", "okay", "tamam", "mashi", "mashy", "تمام شكرا", "تمام ان شاء الله", "ان شاء الله", "ماشي ان شاء الله", "تمام ماشي"];

    /// <summary>A bare thank-you or "ok" gets a short acknowledgement, not a repeat of the offer. Not used when our last
    /// message asked the customer something, because then "تمام" may be the answer.</summary>
    public static AiDraftProposal? Thanks(string text, AiSalesContext context)
    {
        var plain = Regex.Replace(text, @"[^\p{L}\s]", " ").Trim();
        plain = Regex.Replace(plain, @"\s+", " ");
        plain = Regex.Replace(plain, @"\s*(يا فندم|يا استاذه|يا استاذ|يا باشا|وكل الاحترام|ليكي|ليك|جدا|اوي|كتير|خالص)$", "").Trim();
        var isThanks = ThanksWords.Any(w => plain == w || plain.StartsWith(w + " ") && plain.Split(' ').Length <= 4);
        var isOk = OkWords.Contains(plain);
        if (!isThanks && !isOk) return null;
        var previous = context.RecentMessages.LastOrDefault(m => m.Speaker != "customer")?.Text?.TrimEnd() ?? "";
        if (isOk && (previous.EndsWith('؟') || previous.EndsWith('?'))) return null;
        return new(isThanks ? "العفو يا فندم 🙏" : "تمام يا فندم 👍", "acknowledgement", "Short thanks/ok acknowledged without repeating the offer.", false, [], "local-policy");
    }

    private static bool Has(string text, params string[] values) => values.Any(text.Contains);
    private static AiDraftProposal Local(string reply, string intent, string note, Guid[]? facts = null)
        => new(reply, intent, note, true, facts ?? [], "local-policy");
}
