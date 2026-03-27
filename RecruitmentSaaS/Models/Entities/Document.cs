using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("Documents", Schema = "demorecruitment")]
[Index("CandidateId", Name = "IX_demorecruitment_Doc_Cnd")]
public partial class Document
{
    [Key]
    public Guid Id { get; set; }

    public Guid CandidateId { get; set; }

    public Guid UploadedById { get; set; }

    public byte DocumentType { get; set; }

    [StringLength(255)]
    public string FileName { get; set; } = null!;

    [Column("S3Key")]
    [StringLength(500)]
    public string S3key { get; set; } = null!;

    public int FileSizeBytes { get; set; }

    [StringLength(100)]
    public string MimeType { get; set; } = null!;

    [Precision(0)]
    public DateTime UploadedAt { get; set; }

    [ForeignKey("CandidateId")]
    [InverseProperty("Documents")]
    public virtual Candidate Candidate { get; set; } = null!;

    [ForeignKey("UploadedById")]
    [InverseProperty("Documents")]
    public virtual User UploadedBy { get; set; } = null!;
}
