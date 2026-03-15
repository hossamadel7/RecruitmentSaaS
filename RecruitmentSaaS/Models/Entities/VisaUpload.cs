using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("VisaUploads", Schema = "demorecruitment")]
public partial class VisaUpload
{
    [Key]
    public Guid Id { get; set; }

    public Guid UploadedById { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime UploadedAt { get; set; }

    [StringLength(500)]
    public string FileName { get; set; } = null!;

    [StringLength(500)]
    public string FileKey { get; set; } = null!;

    [StringLength(100)]
    public string? ExtractedPassportNo { get; set; }

    [StringLength(100)]
    public string? ExtractedVisaNo { get; set; }

    public DateOnly? ExtractedVisaExpiry { get; set; }

    [StringLength(200)]
    public string? ExtractedFullName { get; set; }

    public byte MatchStatus { get; set; }

    public Guid? MatchedCandidateId { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    [ForeignKey("MatchedCandidateId")]
    [InverseProperty("VisaUploads")]
    public virtual Candidate? MatchedCandidate { get; set; }

    [ForeignKey("UploadedById")]
    [InverseProperty("VisaUploads")]
    public virtual User UploadedBy { get; set; } = null!;
}
