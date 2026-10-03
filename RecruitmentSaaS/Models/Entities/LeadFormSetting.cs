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

    public DateTime? UpdatedAt { get; set; }

    public Guid? UpdatedById { get; set; }
}
