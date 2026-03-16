using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class CandidateActivity
{
    public Guid Id { get; set; }

    public Guid CandidateId { get; set; }

    public byte ActivityType { get; set; }

    public string? Description { get; set; }

    public string? Details { get; set; }

    public Guid? CreatedById { get; set; }

    public string? CreatedByName { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Candidate Candidate { get; set; } = null!;

    public virtual User? CreatedBy { get; set; }
}
