using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("PassportDownloadedCandidates", Schema = "demorecruitment")]
[Index("CandidateId", Name = "UQ_PDC_Candidate", IsUnique = true)]
public partial class PassportDownloadedCandidate
{
    [Key]
    public Guid Id { get; set; }

    public Guid LogId { get; set; }

    public Guid CandidateId { get; set; }

    public Guid? DocumentId { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime DownloadedAt { get; set; }

    [ForeignKey("CandidateId")]
    [InverseProperty("PassportDownloadedCandidate")]
    public virtual Candidate Candidate { get; set; } = null!;

    [ForeignKey("LogId")]
    [InverseProperty("PassportDownloadedCandidates")]
    public virtual PassportDownloadLog Log { get; set; } = null!;
}
