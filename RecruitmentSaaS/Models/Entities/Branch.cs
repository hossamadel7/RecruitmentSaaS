using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("Branches", Schema = "demorecruitment")]
public partial class Branch
{
    [Key]
    public Guid Id { get; set; }

    [StringLength(200)]
    public string Name { get; set; } = null!;

    [StringLength(100)]
    public string? City { get; set; }

    [StringLength(500)]
    public string? Address { get; set; }

    public bool IsActive { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    [InverseProperty("Branch")]
    public virtual ICollection<Candidate> Candidates { get; set; } = new List<Candidate>();

    [InverseProperty("Branch")]
    public virtual ICollection<LeadVisit> LeadVisits { get; set; } = new List<LeadVisit>();

    [InverseProperty("Branch")]
    public virtual ICollection<Lead> Leads { get; set; } = new List<Lead>();

    [InverseProperty("Branch")]
    public virtual ICollection<User> Users { get; set; } = new List<User>();
}
