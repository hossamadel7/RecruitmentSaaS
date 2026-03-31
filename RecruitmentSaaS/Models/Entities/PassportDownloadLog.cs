using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class PassportDownloadLog
{
    public Guid Id { get; set; }

    public Guid PackageId { get; set; }

    public Guid DownloadedById { get; set; }

    public DateTime DownloadedAt { get; set; }

    public int PassportCount { get; set; }

    public bool IsNewOnly { get; set; }

    public virtual User DownloadedBy { get; set; } = null!;

    public virtual JobPackage Package { get; set; } = null!;

    public virtual ICollection<PassportDownloadedCandidate> PassportDownloadedCandidates { get; set; } = new List<PassportDownloadedCandidate>();
}
