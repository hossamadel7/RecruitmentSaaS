using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class PackageStage
{
    public Guid Id { get; set; }

    public Guid PackageId { get; set; }

    public string StageName { get; set; } = null!;

    public int StageOrder { get; set; }

    public decimal? RequiredMinPaymentEgp { get; set; }

    public string? Description { get; set; }

    public bool NotifySalesOnEnter { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public Guid? StageTypeId { get; set; }

    public bool NotifyAdminOnEnter { get; set; }

    public bool RequiresAdminApproval { get; set; }

    public virtual ICollection<CandidateStageHistory> CandidateStageHistoryFromStageNavigations { get; set; } = new List<CandidateStageHistory>();

    public virtual ICollection<CandidateStageHistory> CandidateStageHistoryToStageNavigations { get; set; } = new List<CandidateStageHistory>();

    public virtual ICollection<Candidate> Candidates { get; set; } = new List<Candidate>();

    public virtual JobPackage Package { get; set; } = null!;

    public virtual ICollection<StageActionCompletion> StageActionCompletions { get; set; } = new List<StageActionCompletion>();

    public virtual ICollection<StageApprovalRequest> StageApprovalRequestFromStages { get; set; } = new List<StageApprovalRequest>();

    public virtual ICollection<StageApprovalRequest> StageApprovalRequestToStages { get; set; } = new List<StageApprovalRequest>();

    public virtual StageType? StageType { get; set; }
}
