using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class CandidateStageHistory
{
    public Guid Id { get; set; }

    public Guid CandidateId { get; set; }

    public byte? FromStage { get; set; }

    public byte ToStage { get; set; }

    public Guid ChangedById { get; set; }

    public bool IsOverride { get; set; }

    public string? OverrideReason { get; set; }

    public string? MeetingOutcome { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? FromStageId { get; set; }

    public Guid? ToStageId { get; set; }

    public string? Notes { get; set; }

    public virtual Candidate Candidate { get; set; } = null!;

    public virtual User ChangedBy { get; set; } = null!;

    public virtual PackageStage? FromStageNavigation { get; set; }

    public virtual PackageStage? ToStageNavigation { get; set; }
}
