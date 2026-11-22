using System.Text.RegularExpressions;

namespace RecruitmentSaaS.Services;

/// <summary>
/// Deterministic clean-up of model wording that the prompt alone did not hold: no visit push the customer did not ask
/// for, no customer name in two consecutive replies, no stock "أنا معاك" closers and no "بالنسبة لـ" openers.
/// It only removes or shortens wording; it never adds a business claim.
/// </summary>
public static class AiSalesReplyPolish
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(1);

    // Customer wording (Arabic or Arabizi) that asks about visiting, the office, timing or the next step.
    private static readonly string[] CustomerVisitWords = ["زياره", "ازور", "اجي", "هاجي", "هجي", "جاي", "المكتب", "العنوان", "عنوان", "فين",
        "مكان", "الخطوه", "اقدم", "التقديم", "ابدا", "مواعيد", "امتي", "الساعه", "ميعاد", "موعد", "معاد", "مقابله",
        "agy", "aji", "hagy", "hagi", "gay", "azor", "maktab", "3enwan", "3nwan", "fein", "fen ", "mkan", "makan", "ma3ad", "mi3ad",
        "maw3ed", "sa3a", "emta", "imta", "a2adem", "aqdem", "moqabla", "mo2abla", "interview", "visit", "office", "address", "apply", "next step"];

    // Reply wording that invites or welcomes the customer to a visit.
    private static readonly string[] InvitationWords = ["زياره", "تيجي", "تشرف", "المكتب", "مقابله", "ميعاد", "معاد", "موعد", "نحدد", "تحدد", "تنورنا"];
    private static readonly Regex Welcome = new(@"(اهلا|نورت|مستني|منتظر|نشوف).{0,30}المكتب|المكتب.{0,30}(قريب|نورت)", RegexOptions.CultureInvariant, Limit);
    private static readonly Regex StockCloser = new(@"^و?\s*لو\s+(محتاج|عايز|حابب|عندك|حبيت|احتجت|في|فيه).{0,60}(انا\s+(هنا\s+)?معاك|انا\s+موجود|تحت\s+امرك)",
        RegexOptions.CultureInvariant, Limit);
    private static readonly Regex BareCloser = new(@"^(و\s*)?انا\s+(هنا\s+)?(معاك|موجود|موجوده)\s*$", RegexOptions.CultureInvariant, Limit);
    private static readonly Regex InlineCloser = new(@"[،,]\s*[أا]نا\s+(?:هنا\s+)?معاك(?=\s*[.!؟?]|\s*$)", RegexOptions.CultureInvariant, Limit);
    private static readonly Regex Opener = new(@"(^|[.!؟?]\s+|[،,]\s*)بالنسبة\s*ل(?:ـ)?\s*[^،,.!؟?\n]{1,40}[،,]\s*", RegexOptions.CultureInvariant, Limit);
    private static readonly Regex TitledName = new(@"يا\s+(?:حضرة|حضره|فندم)\s+(?!فندم|حضرتك)(\p{L}{2,})", RegexOptions.CultureInvariant, Limit);
    private static readonly Regex Vocative = new(@"يا\s+(\p{L}{2,})", RegexOptions.CultureInvariant, Limit);
    private static readonly string[] NotNames = ["فندم", "ريت", "رب", "سلام", "خبر", "حلاوه", "جماعه", "باشا", "بيه", "هانم", "حبيبي", "عم", "استاذ",
        "حضرتك", "حضره", "حضرة", "ترى", "اما", "اهلا", "مرحب"];

    public static AiDraftProposal Apply(AiDraftProposal proposal, AiSalesContext context)
    {
        var reply = Polish(proposal.Reply, context);
        return string.IsNullOrWhiteSpace(reply) || reply == proposal.Reply ? proposal : proposal with { Reply = reply };
    }

    public static string Polish(string reply, AiSalesContext context)
    {
        if (string.IsNullOrWhiteSpace(reply)) return reply;
        var customer = IntakeScript.Normalize(context.RecentMessages.LastOrDefault(m => m.Speaker == "customer")?.Text ?? "");
        var askedAboutVisit = CustomerVisitWords.Any(customer.Contains);
        var previous = context.RecentMessages.LastOrDefault(m => m.Speaker != "customer")?.Text ?? "";

        var text = TitledName.Replace(reply, "يا فندم");
        // A name used in the previous reply is not repeated in this one.
        text = Vocative.Replace(text, m =>
        {
            var word = m.Groups[1].Value;
            if (NotNames.Contains(IntakeScript.Normalize(word))) return m.Value;
            return Regex.IsMatch(previous, @"يا\s+(?:حضرة\s+|فندم\s+)?" + Regex.Escape(word) + @"(?!\p{L})", RegexOptions.CultureInvariant, Limit)
                ? "يا فندم" : m.Value;
        });
        text = InlineCloser.Replace(text, "");
        text = Opener.Replace(text, m => m.Groups[1].Value);

        var kept = new List<string>();
        foreach (var sentence in Regex.Split(text, @"(?<=[.!؟?\n])", RegexOptions.CultureInvariant, Limit))
        {
            var norm = IntakeScript.Normalize(sentence).Trim().TrimEnd('.', '!', '؟', '?', '،', ',').Trim();
            if (norm.Length == 0) { kept.Add(sentence); continue; }
            var isQuestion = sentence.TrimEnd().EndsWith('؟') || sentence.TrimEnd().EndsWith('?');
            if (!askedAboutVisit && isQuestion && InvitationWords.Any(norm.Contains)) continue;
            if (!askedAboutVisit && Welcome.IsMatch(norm)) continue;
            if (StockCloser.IsMatch(norm) || BareCloser.IsMatch(norm)) continue;
            kept.Add(sentence);
        }
        var result = Regex.Replace(string.Concat(kept), @"[ \t]{2,}", " ", RegexOptions.CultureInvariant, Limit).Trim();
        // Never leave a reply that is only a greeting fragment.
        return IntakeScript.Normalize(result).Count(char.IsLetter) < 8 ? reply : result;
    }
}
