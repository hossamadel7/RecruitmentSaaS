using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class JobPackage
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string DestinationCountry { get; set; } = null!;

    public string JobTitle { get; set; } = null!;

    public decimal PriceEgp { get; set; }

    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<Candidate> Candidates { get; set; } = new List<Candidate>();

    public virtual ICollection<LeadVisit> LeadVisits { get; set; } = new List<LeadVisit>();

    public virtual ICollection<PackageStage> PackageStages { get; set; } = new List<PackageStage>();

    public virtual ICollection<PassportDownloadLog> PassportDownloadLogs { get; set; } = new List<PassportDownloadLog>();
}
