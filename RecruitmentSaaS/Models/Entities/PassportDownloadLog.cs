using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("PassportDownloadLogs", Schema = "demorecruitment")]
public partial class PassportDownloadLog
{
    [Key]
    public Guid Id { get; set; }

    public Guid PackageId { get; set; }

    public Guid DownloadedById { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime DownloadedAt { get; set; }

    public int PassportCount { get; set; }

    public bool IsNewOnly { get; set; }

    [ForeignKey("DownloadedById")]
    [InverseProperty("PassportDownloadLogs")]
    public virtual User DownloadedBy { get; set; } = null!;

    [ForeignKey("PackageId")]
    [InverseProperty("PassportDownloadLogs")]
    public virtual JobPackage Package { get; set; } = null!;

    [InverseProperty("Log")]
    public virtual ICollection<PassportDownloadedCandidate> PassportDownloadedCandidates { get; set; } = new List<PassportDownloadedCandidate>();
}
