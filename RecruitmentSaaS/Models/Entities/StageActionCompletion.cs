using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class StageActionCompletion
{
    public Guid Id { get; set; }

    public Guid CandidateId { get; set; }

    public Guid PackageStageId { get; set; }

    public DateTime CompletedAt { get; set; }

    public Guid CompletedById { get; set; }

    public byte CompletionType { get; set; }

    public string? Notes { get; set; }

    public virtual Candidate Candidate { get; set; } = null!;

    public virtual User CompletedBy { get; set; } = null!;

    public virtual PackageStage PackageStage { get; set; } = null!;
}
