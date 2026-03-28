using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class Company
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string Country { get; set; } = null!;

    public string? City { get; set; }

    public string? ContactPhone { get; set; }

    public string? ContactEmail { get; set; }

    public DateOnly? ContractStartDate { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; }

    public Guid CreatedById { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<Candidate> Candidates { get; set; } = new List<Candidate>();

    public virtual ICollection<CompanyJob> CompanyJobs { get; set; } = new List<CompanyJob>();

    public virtual User CreatedBy { get; set; } = null!;
}
