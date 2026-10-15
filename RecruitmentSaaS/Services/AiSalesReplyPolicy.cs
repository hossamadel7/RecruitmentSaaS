using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Services;

/// <summary>Conservative draft routing, not a booking or an employment decision.</summary>
public static class AiSalesReplyPolicy
{
    public static AiDraftProposal? Create(AiSalesContext context)
    {
        var latest = context.RecentMessages.LastOrDefault();
        if (latest?.Speaker != "customer" || latest.Type is not ("Text" or "TranscribedAudio")) return null;
        var text = IntakeScript.Normalize(latest.Text);
        // A refusal takes precedence even if the same message also asks for a person.
        if (IntakeScript.NotInterested(text))
            return Local("حاضر يا فندم، نحترم رغبتك في عدم المتابعة.", "stop", "Customer declined contact; staff must handle the stop request.");
        if (IntakeScript.WantsPerson(text))
            return Local("حاضر يا فندم، طلبك محتاج متابعة من موظف.", "handoff", "Customer requested a person; do not condition handoff on another sales question.");

        // Named-day requests require staff/calendar review. Free text is not a confirmed appointment.
        if (text.Contains("الجمعه") && Has(text, "هجيل", "اجي", "ازياره", "ازور", "زياره", "موعد", "معاد", "المكتب"))
        {
            // Only these explicit current Fact formulations support stating Friday closure.
            // No inference from Playbook text, customer history, or the default booking schedule.
            var fact = context.Knowledge.FirstOrDefault(k => k.Kind == AiKnowledgeKind.Fact && k.ValidUntil > DateTime.UtcNow &&
                IntakeScript.Normalize(k.Content).Contains("المكتب") &&
                Has(IntakeScript.Normalize(k.Content), "والجمعه مغلق", "ومغلق يوم الجمعه"));
            return fact == null
                ? Local("موعد الزيارة محتاج مراجعة من الموظف قبل ما نأكد أي حجز يا فندم.", "handoff", "Friday visit requested without an explicit approved closure fact; review the actual office schedule.")
                : Local("المكتب مغلق يوم الجمعة يا فندم. نراجع مع الموظف يوم تاني مناسب للزيارة.", "handoff", "Approved Fact states Friday closure; no appointment created.", [fact.Id]);
        }
        // This is an information handoff, never rejection of the applicant or a hiring decision.
        if (text.Contains("السعوديه"))
            return Local("استفسار حضرتك عن السعودية محتاج مراجعة من مسؤول الشركة قبل ما نأكد تفاصيل أي عرض.", "handoff", "Unsupported market request; do not infer that the company has no other-country vacancies.");
        if (Has(text, "سواق", "سائق") && Has(text, "متاح", "شغل", "وظيفه", "فرصه"))
            return Local("توافر شغل سائق محتاج تأكيد من مسؤول التعاقدات يا فندم، قبل ما نأكد لحضرتك وظيفة معينة.", "handoff", "Specific driving availability requires contracts staff review; no job or future transfer promised.");
        // Route requests for individual matching to a person without deciding from age/nationality.
        // Questions about merely visiting or obtaining information remain ordinary enquiries.
        if (text.Contains("ينفع") && !Has(text, "اجي", "ازور", "زياره", "المكتب", "معاد", "موعد", "اعرف", "مواعيد", "المرتب", "الراتب", "بدفع"))
            return Local("مناسبة العرض لحضرتك محتاجة مراجعة من مسؤول التعاقدات يا فندم قبل ما نأكد أي فرصة.", "handoff", "Individual matching question routed to staff; no eligibility or hiring decision made.");
        return null;
    }

    private static bool Has(string text, params string[] values) => values.Any(text.Contains);
    private static AiDraftProposal Local(string reply, string intent, string note, Guid[]? facts = null)
        => new(reply, intent, note, true, facts ?? [], "local-policy");
}
