using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("JobPackages", Schema = "demorecruitment")]
public partial class JobPackage
{
    [Key]
    public Guid Id { get; set; }

    [StringLength(200)]
    public string Name { get; set; } = null!;

    [StringLength(100)]
    public string DestinationCountry { get; set; } = null!;

    [StringLength(200)]
    public string JobTitle { get; set; } = null!;

    [Column("PriceEGP", TypeName = "decimal(18, 2)")]
    public decimal PriceEgp { get; set; }

    public string? Description { get; set; }

    public bool IsActive { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    [InverseProperty("JobPackage")]
    public virtual ICollection<Candidate> Candidates { get; set; } = new List<Candidate>();

    [InverseProperty("JobPackage")]
    public virtual ICollection<LeadVisit> LeadVisits { get; set; } = new List<LeadVisit>();
}
