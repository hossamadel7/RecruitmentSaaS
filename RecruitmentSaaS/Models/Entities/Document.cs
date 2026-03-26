using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class Document
{
    public Guid Id { get; set; }

    public Guid CandidateId { get; set; }

    public Guid UploadedById { get; set; }

    public byte DocumentType { get; set; }

    public string FileName { get; set; } = null!;

    public string S3key { get; set; } = null!;

    public int FileSizeBytes { get; set; }

    public string MimeType { get; set; } = null!;

    public DateTime UploadedAt { get; set; }

    public virtual Candidate Candidate { get; set; } = null!;

    public virtual User UploadedBy { get; set; } = null!;
}
