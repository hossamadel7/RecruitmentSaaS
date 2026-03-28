using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class VisaUpload
{
    public Guid Id { get; set; }

    public Guid UploadedById { get; set; }

    public DateTime UploadedAt { get; set; }

    public string FileName { get; set; } = null!;

    public string FileKey { get; set; } = null!;

    public string? ExtractedPassportNo { get; set; }

    public string? ExtractedVisaNo { get; set; }

    public DateOnly? ExtractedVisaExpiry { get; set; }

    public string? ExtractedFullName { get; set; }

    public byte MatchStatus { get; set; }

    public Guid? MatchedCandidateId { get; set; }

    public string? Notes { get; set; }

    public virtual Candidate? MatchedCandidate { get; set; }

    public virtual User UploadedBy { get; set; } = null!;
}
