using System;

namespace RecruitmentSaaS.Models.Entities;

/// <summary>
/// One anonymous view of the public registration form (home page, /register or a team link):
/// how far the visitor got and whether they submitted. No name, phone or IP is stored —
/// it only explains where people drop off ("إحصائيات الفورم").
/// </summary>
public class FormVisit
{
    /// <summary>Random id made by the browser for this visit.</summary>
    public Guid Id { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime LastSeenAt { get; set; }

    /// <summary>home | register | team</summary>
    public string Page { get; set; } = "home";

    public string? TeamSlug { get; set; }

    /// <summary>facebook | instagram | google | tiktok | whatsapp | direct | other</summary>
    public string Source { get; set; } = "direct";

    public string? UtmCampaign { get; set; }

    /// <summary>mobile | desktop | tablet</summary>
    public string Device { get; set; } = "mobile";

    /// <summary>Fields the visitor touched, comma separated: name,phone,age,job,notes</summary>
    public string? FieldsTouched { get; set; }

    /// <summary>The last field they touched — where people who leave stop.</summary>
    public string? LastField { get; set; }

    /// <summary>Times the form came back with an error (e.g. wrong phone, no age).</summary>
    public int ErrorCount { get; set; }

    public string? LastError { get; set; }

    public DateTime? SubmittedAt { get; set; }

    /// <summary>1 = new lead, 2 = already registered (same phone)</summary>
    public byte? Outcome { get; set; }

    public Guid? LeadId { get; set; }

    /// <summary>Sent on to WhatsApp after submitting (45+).</summary>
    public bool WhatsAppRedirect { get; set; }
}
