using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class PassportDownloadedCandidate
{
    public Guid Id { get; set; }

    public Guid LogId { get; set; }

    public Guid CandidateId { get; set; }

    public Guid? DocumentId { get; set; }

    public DateTime DownloadedAt { get; set; }

    public virtual Candidate Candidate { get; set; } = null!;

    public virtual PassportDownloadLog Log { get; set; } = null!;
}
