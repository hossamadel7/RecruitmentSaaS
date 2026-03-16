using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class StageApprovalRequest
{
    public Guid Id { get; set; }

    public Guid CandidateId { get; set; }

    public Guid FromStageId { get; set; }

    public Guid ToStageId { get; set; }

    public Guid RequestedById { get; set; }

    public DateTime RequestedAt { get; set; }

    public string? RequestNote { get; set; }

    public byte Status { get; set; }

    public Guid? ReviewedById { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public string? AdminNote { get; set; }

    public byte? ExceptionType { get; set; }

    public decimal? MinPaymentRequired { get; set; }

    public decimal? AmountPaid { get; set; }

    public virtual Candidate Candidate { get; set; } = null!;

    public virtual PackageStage FromStage { get; set; } = null!;

    public virtual User RequestedBy { get; set; } = null!;

    public virtual User? ReviewedBy { get; set; }

    public virtual PackageStage ToStage { get; set; } = null!;
}
