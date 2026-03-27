using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("LeadVisits", Schema = "demorecruitment")]
[Index("BranchId", "VisitDateTime", Name = "IX_demorecruitment_LV_Br", IsDescending = new[] { false, true })]
[Index("LeadId", "CreatedAt", Name = "IX_demorecruitment_LV_Ld", IsDescending = new[] { false, true })]
public partial class LeadVisit
{
    [Key]
    public Guid Id { get; set; }

    public Guid LeadId { get; set; }

    public Guid BranchId { get; set; }

    public Guid ReceptionUserId { get; set; }

    public Guid? AssignedSalesUserId { get; set; }

    [Precision(0)]
    public DateTime VisitDateTime { get; set; }

    public byte MeetingOutcome { get; set; }

    public Guid? ConvertedCandidateId { get; set; }

    public Guid? JobPackageId { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    [ForeignKey("AssignedSalesUserId")]
    [InverseProperty("LeadVisitAssignedSalesUsers")]
    public virtual User? AssignedSalesUser { get; set; }

    [ForeignKey("BranchId")]
    [InverseProperty("LeadVisits")]
    public virtual Branch Branch { get; set; } = null!;

    [ForeignKey("JobPackageId")]
    [InverseProperty("LeadVisits")]
    public virtual JobPackage? JobPackage { get; set; }

    [ForeignKey("LeadId")]
    [InverseProperty("LeadVisits")]
    public virtual Lead Lead { get; set; } = null!;

    [ForeignKey("ReceptionUserId")]
    [InverseProperty("LeadVisitReceptionUsers")]
    public virtual User ReceptionUser { get; set; } = null!;
}
