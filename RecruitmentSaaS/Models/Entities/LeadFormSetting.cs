using System;

namespace RecruitmentSaaS.Models.Entities;

/// <summary>
/// Admin-editable settings for the public lead registration form. Single row.
/// Applicants whose selected age is >= SeniorAgeThreshold are sent straight to a
/// WhatsApp chat with SalesWhatsAppNumber after submitting the form.
/// </summary>
public partial class LeadFormSetting
{
    public Guid Id { get; set; }

    public byte SeniorAgeThreshold { get; set; }

    /// <summary>International format, digits only (e.g. 201012345678). Empty disables the redirect.</summary>
    public string? SalesWhatsAppNumber { get; set; }

    /// <summary>Pre-filled message; {name}, {age}, {job} are replaced with the applicant's data.</summary>
    public string? SalesWhatsAppMessage { get; set; }

    /// <summary>Send WelcomeMessage automatically when a lead's WhatsApp chat starts.</summary>
    public bool WelcomeMessageEnabled { get; set; }

    /// <summary>{agent} = the assigned TeleSales' Arabic name, {name} = the customer's name.</summary>
    public string? WelcomeMessage { get; set; }

    public const string DefaultWelcomeMessage = """
أهلاً بحضرتك ❤️ نورتنا وشرفتنا

معاكِ أ/ (*{agent}*) من شركة *الفهد العربي لإلحاق العمالة بالخارج* 🇪🇬
ترخيص رقم *492*

يسعدنا نساعد حضرتك ونشوف أنسب فرصة عمل متاحة ليك حسب السن والمهنة والخبرة .

علشان نقدر نوجّهك بشكل صحيح ونوضح لحضرتك التفاصيل، محتاجين من حضرتك البيانات البسيطة دي:

• *الاسم ثلاثي:*
• *المهنة في جواز السفر:*
• *السن:*
• *هل سبق لك السفر للعمل بالخارج؟*
• *هل تقبل السفر خلال شهر؟*
• ⁠*رقم التواصل*؟

ابعتلنا البيانات، وأنا هتابع مع حضرتك بنفسي وأوضحلك الفرص المناسبة والتفاصيل كاملة.

*مستنيين بيانات حضرتك، وإن شاء الله نقدر نساعدك في الوصول لفرصة مناسبة ليك.*
""";

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedById { get; set; }
}
