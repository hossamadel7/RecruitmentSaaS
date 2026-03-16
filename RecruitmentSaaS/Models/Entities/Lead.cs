using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class Lead
{
    public Guid Id { get; set; }

    public Guid BranchId { get; set; }

    public Guid? AssignedSalesId { get; set; }

    public Guid? CampaignId { get; set; }

    public Guid RegisteredById { get; set; }

    public string FullName { get; set; } = null!;

    public string Phone { get; set; } = null!;

    public byte LeadSource { get; set; }

    public byte Status { get; set; }

    public string? Notes { get; set; }

    public string? InterestedJobTitle { get; set; }

    public string? InterestedCountry { get; set; }

    public string? ReferredByName { get; set; }

    public string? ReferredByPhone { get; set; }

    public bool IsConverted { get; set; }

    public DateTime? ConvertedAt { get; set; }

    public Guid? ConvertedCandidateId { get; set; }

    public string? FacebookLeadId { get; set; }

    public string? FacebookFormId { get; set; }

    public string? UtmSource { get; set; }

    public string? UtmMedium { get; set; }

    public string? UtmCampaign { get; set; }

    public string? UtmContent { get; set; }

    public Guid? DuplicateOfLeadId { get; set; }

    public bool IsDuplicate { get; set; }

    public int LeadSequence { get; set; }

    public string? LeadCode { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? LastContactedAt { get; set; }

    public Guid? AssignedOfficeSalesId { get; set; }

    public DateTime? AppointmentDate { get; set; }

    public virtual User? AssignedOfficeSales { get; set; }

    public virtual User? AssignedSales { get; set; }

    public virtual Branch Branch { get; set; } = null!;

    public virtual Campaign? Campaign { get; set; }

    public virtual ICollection<FollowUpReminder> FollowUpReminders { get; set; } = new List<FollowUpReminder>();

    public virtual ICollection<LeadActivity> LeadActivities { get; set; } = new List<LeadActivity>();

    public virtual ICollection<LeadCallLog> LeadCallLogs { get; set; } = new List<LeadCallLog>();

    public virtual ICollection<LeadFunnelHistory> LeadFunnelHistories { get; set; } = new List<LeadFunnelHistory>();

    public virtual ICollection<LeadVisit> LeadVisits { get; set; } = new List<LeadVisit>();

    public virtual User RegisteredBy { get; set; } = null!;
}
