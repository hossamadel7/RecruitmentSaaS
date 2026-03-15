using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("StageApprovalRequests", Schema = "demorecruitment")]
public partial class StageApprovalRequest
{
    [Key]
    public Guid Id { get; set; }

    public Guid CandidateId { get; set; }

    public Guid FromStageId { get; set; }

    public Guid ToStageId { get; set; }

    public Guid RequestedById { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime RequestedAt { get; set; }

    [StringLength(500)]
    public string? RequestNote { get; set; }

    public byte Status { get; set; }

    public Guid? ReviewedById { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? ReviewedAt { get; set; }

    [StringLength(500)]
    public string? AdminNote { get; set; }

    public byte? ExceptionType { get; set; }

    [Column(TypeName = "decimal(18, 2)")]
    public decimal? MinPaymentRequired { get; set; }

    [Column(TypeName = "decimal(18, 2)")]
    public decimal? AmountPaid { get; set; }

    [ForeignKey("CandidateId")]
    [InverseProperty("StageApprovalRequests")]
    public virtual Candidate Candidate { get; set; } = null!;

    [ForeignKey("FromStageId")]
    [InverseProperty("StageApprovalRequestFromStages")]
    public virtual PackageStage FromStage { get; set; } = null!;

    [ForeignKey("RequestedById")]
    [InverseProperty("StageApprovalRequestRequestedBies")]
    public virtual User RequestedBy { get; set; } = null!;

    [ForeignKey("ReviewedById")]
    [InverseProperty("StageApprovalRequestReviewedBies")]
    public virtual User? ReviewedBy { get; set; }

    [ForeignKey("ToStageId")]
    [InverseProperty("StageApprovalRequestToStages")]
    public virtual PackageStage ToStage { get; set; } = null!;
}
