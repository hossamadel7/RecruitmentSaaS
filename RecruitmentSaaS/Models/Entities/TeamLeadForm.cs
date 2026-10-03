using System;

namespace RecruitmentSaaS.Models.Entities;

/// <summary>
/// A TeleSales team's own public registration form (/register/{Slug}). The team is the
/// TeleSales Manager (Role 7) plus the TeleSales users whose ManagerId points to them.
/// The age threshold is global (LeadFormSetting); only the WhatsApp number is per team.
/// </summary>
public partial class TeamLeadForm
{
    public Guid Id { get; set; }

    public Guid ManagerId { get; set; }

    public string Slug { get; set; } = null!;

    /// <summary>International format, digits only. Empty falls back to the global sales number.</summary>
    public string? WhatsAppNumber { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual User Manager { get; set; } = null!;
}
