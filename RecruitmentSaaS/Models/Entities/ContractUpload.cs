using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("ContractUploads", Schema = "demorecruitment")]
[Index("MatchStatus", Name = "IX_demorecruitment_ContractUploads_Status")]
public partial class ContractUpload
{
    [Key]
    public Guid Id { get; set; }

    public Guid UploadedById { get; set; }

    [StringLength(255)]
    public string FileName { get; set; } = null!;

    [StringLength(500)]
    public string FileKey { get; set; } = null!;

    [StringLength(50)]
    public string? ExtractedPassportNo { get; set; }

    [StringLength(200)]
    public string? ExtractedEmployerName { get; set; }

    [StringLength(100)]
    public string? ExtractedTransactionNo { get; set; }

    public byte MatchStatus { get; set; }

    public Guid? MatchedCandidateId { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    [ForeignKey("MatchedCandidateId")]
    [InverseProperty("ContractUploads")]
    public virtual Candidate? MatchedCandidate { get; set; }

    [ForeignKey("UploadedById")]
    [InverseProperty("ContractUploads")]
    public virtual User UploadedBy { get; set; } = null!;
}
