using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("PackageStages", Schema = "demorecruitment")]
[Index("PackageId", "StageOrder", Name = "IX_demo_PS_PackageId")]
[Index("PackageId", "StageOrder", Name = "UQ_demo_PS_Order", IsUnique = true)]
public partial class PackageStage
{
    [Key]
    public Guid Id { get; set; }

    public Guid PackageId { get; set; }

    [StringLength(200)]
    public string StageName { get; set; } = null!;

    public int StageOrder { get; set; }

    [Column("RequiredMinPaymentEGP", TypeName = "decimal(18, 2)")]
    public decimal? RequiredMinPaymentEgp { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    public bool NotifySalesOnEnter { get; set; }

    public bool IsActive { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    public Guid? StageTypeId { get; set; }

    public bool NotifyAdminOnEnter { get; set; }

    public bool RequiresAdminApproval { get; set; }

    [InverseProperty("FromStageNavigation")]
    public virtual ICollection<CandidateStageHistory> CandidateStageHistoryFromStageNavigations { get; set; } = new List<CandidateStageHistory>();

    [InverseProperty("ToStageNavigation")]
    public virtual ICollection<CandidateStageHistory> CandidateStageHistoryToStageNavigations { get; set; } = new List<CandidateStageHistory>();

    [InverseProperty("CurrentPackageStage")]
    public virtual ICollection<Candidate> Candidates { get; set; } = new List<Candidate>();

    [ForeignKey("PackageId")]
    [InverseProperty("PackageStages")]
    public virtual JobPackage Package { get; set; } = null!;

    [InverseProperty("PackageStage")]
    public virtual ICollection<StageActionCompletion> StageActionCompletions { get; set; } = new List<StageActionCompletion>();

    [InverseProperty("FromStage")]
    public virtual ICollection<StageApprovalRequest> StageApprovalRequestFromStages { get; set; } = new List<StageApprovalRequest>();

    [InverseProperty("ToStage")]
    public virtual ICollection<StageApprovalRequest> StageApprovalRequestToStages { get; set; } = new List<StageApprovalRequest>();

    [ForeignKey("StageTypeId")]
    [InverseProperty("PackageStages")]
    public virtual StageType? StageType { get; set; }
}
