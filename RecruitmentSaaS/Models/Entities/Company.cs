using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("Companies", Schema = "demorecruitment")]
public partial class Company
{
    [Key]
    public Guid Id { get; set; }

    [StringLength(200)]
    public string Name { get; set; } = null!;

    [StringLength(100)]
    public string Country { get; set; } = null!;

    [StringLength(100)]
    public string? City { get; set; }

    [StringLength(50)]
    public string? ContactPhone { get; set; }

    [StringLength(255)]
    public string? ContactEmail { get; set; }

    public DateOnly? ContractStartDate { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; }

    public Guid CreatedById { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    [Precision(0)]
    public DateTime? UpdatedAt { get; set; }

    [InverseProperty("Company")]
    public virtual ICollection<Candidate> Candidates { get; set; } = new List<Candidate>();

    [InverseProperty("Company")]
    public virtual ICollection<CompanyJob> CompanyJobs { get; set; } = new List<CompanyJob>();

    [ForeignKey("CreatedById")]
    [InverseProperty("Companies")]
    public virtual User CreatedBy { get; set; } = null!;
}
