using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("Leads", Schema = "demorecruitment")]
[Index("CampaignId", Name = "IX_demorecruitment_Ld_Ca")]
[Index("AssignedOfficeSalesId", Name = "IX_demorecruitment_Ld_OfSa")]
[Index("Phone", Name = "IX_demorecruitment_Ld_Ph")]
[Index("AssignedSalesId", Name = "IX_demorecruitment_Ld_Sa")]
[Index("LeadSource", Name = "IX_demorecruitment_Ld_Src")]
[Index("Status", Name = "IX_demorecruitment_Ld_St")]
[Index("LeadCode", Name = "UQ_demorecruitment_Ld_Code", IsUnique = true)]
[Index("Phone", Name = "UQ_demorecruitment_Ld_Ph", IsUnique = true)]
public partial class Lead
{
    [Key]
    public Guid Id { get; set; }

    public Guid BranchId { get; set; }

    public Guid? AssignedSalesId { get; set; }

    public Guid? CampaignId { get; set; }

    public Guid RegisteredById { get; set; }

    [StringLength(200)]
    public string FullName { get; set; } = null!;

    [StringLength(30)]
    public string Phone { get; set; } = null!;

    public byte LeadSource { get; set; }

    public byte Status { get; set; }

    public string? Notes { get; set; }

    [StringLength(200)]
    public string? InterestedJobTitle { get; set; }

    [StringLength(100)]
    public string? InterestedCountry { get; set; }

    [StringLength(200)]
    public string? ReferredByName { get; set; }

    [StringLength(30)]
    public string? ReferredByPhone { get; set; }

    public bool IsConverted { get; set; }

    [Precision(0)]
    public DateTime? ConvertedAt { get; set; }

    public Guid? ConvertedCandidateId { get; set; }

    [StringLength(100)]
    public string? FacebookLeadId { get; set; }

    [StringLength(100)]
    public string? FacebookFormId { get; set; }

    [StringLength(100)]
    public string? UtmSource { get; set; }

    [StringLength(100)]
    public string? UtmMedium { get; set; }

    [StringLength(200)]
    public string? UtmCampaign { get; set; }

    [StringLength(200)]
    public string? UtmContent { get; set; }

    public Guid? DuplicateOfLeadId { get; set; }

    public bool IsDuplicate { get; set; }

    public int LeadSequence { get; set; }

    [StringLength(8)]
    public string? LeadCode { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    [Precision(0)]
    public DateTime? UpdatedAt { get; set; }

    [Precision(0)]
    public DateTime? LastContactedAt { get; set; }

    public Guid? AssignedOfficeSalesId { get; set; }

    [Precision(0)]
    public DateTime? AppointmentDate { get; set; }

    public Guid? GoogleSheetId { get; set; }

    [ForeignKey("AssignedOfficeSalesId")]
    [InverseProperty("LeadAssignedOfficeSales")]
    public virtual User? AssignedOfficeSales { get; set; }

    [ForeignKey("AssignedSalesId")]
    [InverseProperty("LeadAssignedSales")]
    public virtual User? AssignedSales { get; set; }

    [ForeignKey("BranchId")]
    [InverseProperty("Leads")]
    public virtual Branch Branch { get; set; } = null!;

    [ForeignKey("CampaignId")]
    [InverseProperty("Leads")]
    public virtual Campaign? Campaign { get; set; }

    [InverseProperty("Lead")]
    public virtual ICollection<FollowUpReminder> FollowUpReminders { get; set; } = new List<FollowUpReminder>();

    [ForeignKey("GoogleSheetId")]
    [InverseProperty("Leads")]
    public virtual SalesGoogleSheet? GoogleSheet { get; set; }

    [InverseProperty("Lead")]
    public virtual ICollection<LeadActivity> LeadActivities { get; set; } = new List<LeadActivity>();

    [InverseProperty("Lead")]
    public virtual ICollection<LeadCallLog> LeadCallLogs { get; set; } = new List<LeadCallLog>();

    [InverseProperty("Lead")]
    public virtual ICollection<LeadFunnelHistory> LeadFunnelHistories { get; set; } = new List<LeadFunnelHistory>();

    [InverseProperty("Lead")]
    public virtual ICollection<LeadVisit> LeadVisits { get; set; } = new List<LeadVisit>();

    [ForeignKey("RegisteredById")]
    [InverseProperty("LeadRegisteredBies")]
    public virtual User RegisteredBy { get; set; } = null!;
}
