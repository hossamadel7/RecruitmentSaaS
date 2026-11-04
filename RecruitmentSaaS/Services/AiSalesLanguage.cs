using System.Text.RegularExpressions;
namespace RecruitmentSaaS.Services;
public static class AiSalesLanguage
{
    public static string Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "ar-EG";
        if (Regex.IsMatch(text, @"[\u0600-\u06ff]"))
            return Regex.IsMatch(text, "أرغب|ارغب|يرجى|أريد|اريد|هل تتوفر|ما هي|ماهي|لا أرغب|لا ارغب") ? "ar" : "ar-EG";
        return Regex.IsMatch(text, @"(?i)\b(?:ana|3ayz|3awez|3ayez|3nd|enta|ezay|ezai|eh|shoghl|ma3|bkam|mesh|msh|momken|law|feen)\b|[a-z][237][a-z]") ? "arabizi" : "en";
    }
    public static string SafeLocal(string intent, string language) => (language, intent) switch
    {
        ("en", "stop") => "Understood. We respect your request not to continue contact.",
        ("en", "handoff") => "A staff member needs to review this before we confirm any details.",
        ("en", "thinking") => "Of course, take your time. I'm here if you need anything clarified.",
        ("en", _) => "Could you explain your request in writing so we can help accurately?",
        ("arabizi", "stop") => "Tamam ya fandem, han7tarem raghtak enna manetabe3sh.",
        ("arabizi", "handoff") => "La zem el mas2ool yerage3 el tafaseel abl ma ne2akked ay ma3looma.",
        ("arabizi", "thinking") => "Tamam ya fandem, khod wa2tak. Ana ma3ak law me7tag tawdee7.",
        ("arabizi", _) => "Momken tewadda7 talab 7adretak bel ketaba?",
        ("ar", "stop") => "حاضر، نحترم رغبتك في عدم مواصلة التواصل.",
        ("ar", "handoff") => "يحتاج هذا الطلب إلى مراجعة موظف قبل تأكيد التفاصيل.",
        ("ar", "thinking") => "بالطبع، خذ وقتك. يمكنني توضيح أي نقطة عند الحاجة.",
        ("ar", _) => "هل يمكنك توضيح طلبك كتابةً لنساعدك بدقة؟",
        (_, "stop") => "حاضر يا فندم، نحترم رغبتك في عدم المتابعة.",
        (_, "handoff") => "محتاجين نراجع المعلومة مع موظف قبل ما نأكد أي تفاصيل.",
        (_, "thinking") => "تمام يا فندم، خد وقتك. لو احتجت توضيح أنا معاك.",
        _ => "ممكن توضح طلبك بالكتابة عشان نقدر نساعدك بدقة؟"
    };
}
