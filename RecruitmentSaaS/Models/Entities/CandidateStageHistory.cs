using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("CandidateStageHistory", Schema = "demorecruitment")]
[Index("CandidateId", "CreatedAt", Name = "IX_demorecruitment_CSH_Cnd", IsDescending = new[] { false, true })]
public partial class CandidateStageHistory
{
    [Key]
    public Guid Id { get; set; }

    public Guid CandidateId { get; set; }

    public byte? FromStage { get; set; }

    public byte ToStage { get; set; }

    public Guid ChangedById { get; set; }

    public bool IsOverride { get; set; }

    [StringLength(500)]
    public string? OverrideReason { get; set; }

    public string? MeetingOutcome { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    [ForeignKey("CandidateId")]
    [InverseProperty("CandidateStageHistories")]
    public virtual Candidate Candidate { get; set; } = null!;

    [ForeignKey("ChangedById")]
    [InverseProperty("CandidateStageHistories")]
    public virtual User ChangedBy { get; set; } = null!;
}
