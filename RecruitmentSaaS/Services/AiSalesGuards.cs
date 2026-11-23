using System.Text.RegularExpressions;

namespace RecruitmentSaaS.Services;

/// <summary>Deterministic checks around the model: spam turns, invented jobs, and "any day" answers.</summary>
public static class AiSalesGuards
{
    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(200);
    private static readonly Regex Link = new(@"https?://|www\.|\b[\w-]+\.(?:buzz|xyz|top|click|link|site|online|shop|live|icu|cc)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Limit);
    private static readonly string[] SafeLinks = ["maps.google", "goo.gl/maps", "maps.app.goo.gl", "thearabianfahd.com"];
    private static readonly string[] BaitWords = ["استلمت", "جرب", "اربح", "ربحت", "كسبت", "مكسب", "هديه", "مجانا", "سهل جدا",
        "اضغط", "سجل الان", "بونص", "جنيه!", "دولار!", "شحن رصيد", "رصيد مجاني"];

    /// <summary>A scam/bait message with a foreign link (e.g. "أنا استلمت 3,000 جنيه! جرّب أنت كمان + link"): never answered.</summary>
    public static bool IsSpam(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !Link.IsMatch(text)) return false;
        var lower = text.ToLowerInvariant();
        if (SafeLinks.Any(lower.Contains) && !Regex.IsMatch(lower, @"\.(?:buzz|xyz|top|click|icu)\b", RegexOptions.None, Limit)) return false;
        var normalized = IntakeScript.Normalize(text);
        return BaitWords.Any(w => normalized.Contains(IntakeScript.Normalize(w)));
    }

    // Each group: the label used in a reply, then the word stems that name the job (normalized spelling).
    private static readonly (string Label, string[] Stems)[] Jobs =
    [
        ("التدريس", ["مدرس", "تدريس", "معلم", "مدرسه"]),
        ("التمريض", ["ممرض", "تمريض"]),
        ("الطب", ["طبيب", "دكتور", "اطباء"]),
        ("المحاسبة", ["محاسب", "محاسبه"]),
        ("الهندسة", ["مهندس", "هندسه"]),
        ("الأمن والحراسة", ["حارس", "حراسه", "سكيوريتي", "فرد امن", "افراد امن", "مشرف امن"]),
        ("السواقة", ["سواق", "سائق", "سواقه"]),
        ("البرمجة", ["مبرمج", "برمجه"]),
        ("المحاماة", ["محامي", "محاماه"]),
        ("الصيدلة", ["صيدلي", "صيدله"]),
        ("الحلاقة", ["حلاق", "حلاقه"]),
        ("الخياطة", ["خياط", "خياطه"]),
        ("الطبخ", ["شيف", "طباخ", "طبخ"]),
        ("الكهرباء", ["كهربائي", "كهربا", "كهرباء"]),
        ("الدليفري", ["دليفري", "توصيل", "مندوب"]),
    ];
    private static readonly string[] Negations = ["مش متاح", "مش موجود", "مفيش", "غير متاح", "غير موجود", "مش متوفر", "للاسف", "مش ضمن", "لا يوجد"];

    /// <summary>
    /// The job a reply says is available although no approved fact names it (e.g. "عقود تدريس في تركيا"), or null.
    /// A reply that says the job is not available is fine.
    /// </summary>
    public static string? UnlistedJobClaim(string reply, IEnumerable<string> factContents)
    {
        var text = IntakeScript.Normalize(reply);
        if (Negations.Any(n => text.Contains(IntakeScript.Normalize(n)))) return null;
        var facts = IntakeScript.Normalize(string.Join(" ", factContents));
        foreach (var (label, stems) in Jobs)
        {
            var named = stems.Select(IntakeScript.Normalize).ToArray();
            if (named.Any(s => Mentions(text, s)) && !named.Any(s => Mentions(facts, s))) return label;
        }
        return null;
    }

    private static bool Mentions(string text, string stem)
        => Regex.IsMatch(text, @"(?<!\p{L})(?:ال|بال|وال|لل|و|ب|ل)?" + Regex.Escape(stem), RegexOptions.CultureInvariant, Limit);

    /// <summary>The safe reply when the model keeps offering a job that no fact lists.</summary>
    public static string UnlistedJobReply(string label)
        => $"حالياً مفيش عقود {label} متاحة عندنا يا فندم، بس عندنا فرص شغل تانية كتير. تحب أقولك على المتاح المناسب لحضرتك؟";

    private static readonly Regex AnyDayText = new(
        @"^(?:اي|ايه|اى)\s*(?:يوم|وقت|معاد|ميعاد|موعد)(?:\s*(?:مناسب|يناسبك|تحبيه|تحبوه|براحتك|عادي))?$|^(?:ay|any|ai)\s*(?:yom|youm|yoom|day|wa2t|wa2at|time)$|^(?:في|فى)\s*(?:اي|اى)\s*(?:يوم|وقت)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Limit);
    private static readonly string[] DayNames = ["الأحد", "الاتنين", "التلات", "الأربع", "الخميس", "الجمعة", "السبت"];

    /// <summary>"أي يوم" after Gihan asked which day: suggest tomorrow (Saturday instead of Friday).</summary>
    public static AiDraftProposal? AnyDay(string normalizedText, AiSalesContext context, DateTime todayCairo)
    {
        var plain = Regex.Replace(normalizedText, @"[^\p{L}\p{N}\s]", " ", RegexOptions.None, Limit).Trim();
        plain = Regex.Replace(plain, @"\s+", " ", RegexOptions.None, Limit);
        plain = Regex.Replace(plain, @"\s*(?:يا فندم|يا استاذه|يا استاذ|يا باشا)$", "", RegexOptions.None, Limit).Trim();
        if (!AnyDayText.IsMatch(plain)) return null;
        var previous = IntakeScript.Normalize(context.RecentMessages.LastOrDefault(m => m.Speaker != "customer")?.Text);
        if (!previous.Contains("يوم") && !previous.Contains("امتي") && !previous.Contains("معاد")) return null;
        var day = todayCairo.Date.AddDays(1);
        if (day.DayOfWeek == DayOfWeek.Friday) day = day.AddDays(1);
        var when = day == todayCairo.Date.AddDays(1) ? "بكرة " : "";
        return new($"تمام يا فندم، إيه رأيك تيجي {when}يوم {DayNames[(int)day.DayOfWeek]}؟", "qualification",
            "Customer is free any day; suggested the next open day.", false, [], "local-policy");
    }
}
