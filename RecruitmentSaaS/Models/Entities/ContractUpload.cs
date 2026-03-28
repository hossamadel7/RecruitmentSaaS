using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class ContractUpload
{
    public Guid Id { get; set; }

    public Guid UploadedById { get; set; }

    public string FileName { get; set; } = null!;

    public string FileKey { get; set; } = null!;

    public string? ExtractedPassportNo { get; set; }

    public string? ExtractedEmployerName { get; set; }

    public string? ExtractedTransactionNo { get; set; }

    public byte MatchStatus { get; set; }

    public Guid? MatchedCandidateId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Candidate? MatchedCandidate { get; set; }

    public virtual User UploadedBy { get; set; } = null!;
}
