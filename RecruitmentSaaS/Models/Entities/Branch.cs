using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class Branch
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string? City { get; set; }

    public string? Address { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<Candidate> Candidates { get; set; } = new List<Candidate>();

    public virtual ICollection<LeadVisit> LeadVisits { get; set; } = new List<LeadVisit>();

    public virtual ICollection<Lead> Leads { get; set; } = new List<Lead>();

    public virtual ICollection<User> Users { get; set; } = new List<User>();
}
