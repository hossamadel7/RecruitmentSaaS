using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("StageActionCompletions", Schema = "demorecruitment")]
[Index("CandidateId", "PackageStageId", Name = "UQ_SAC_CandStage", IsUnique = true)]
public partial class StageActionCompletion
{
    [Key]
    public Guid Id { get; set; }

    public Guid CandidateId { get; set; }

    public Guid PackageStageId { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CompletedAt { get; set; }

    public Guid CompletedById { get; set; }

    public byte CompletionType { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    [ForeignKey("CandidateId")]
    [InverseProperty("StageActionCompletions")]
    public virtual Candidate Candidate { get; set; } = null!;

    [ForeignKey("CompletedById")]
    [InverseProperty("StageActionCompletions")]
    public virtual User CompletedBy { get; set; } = null!;

    [ForeignKey("PackageStageId")]
    [InverseProperty("StageActionCompletions")]
    public virtual PackageStage PackageStage { get; set; } = null!;
}
