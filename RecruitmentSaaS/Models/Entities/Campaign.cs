using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("Campaigns", Schema = "demorecruitment")]
[Index("Status", Name = "IX_demorecruitment_Ca_St")]
public partial class Campaign
{
    [Key]
    public Guid Id { get; set; }

    [StringLength(200)]
    public string Name { get; set; } = null!;

    public byte Source { get; set; }

    public byte Status { get; set; }

    [Column("BudgetEGP", TypeName = "decimal(18, 2)")]
    public decimal? BudgetEgp { get; set; }

    [Column("SpendEGP", TypeName = "decimal(18, 2)")]
    public decimal? SpendEgp { get; set; }

    [StringLength(100)]
    public string? FacebookCampaignId { get; set; }

    [StringLength(100)]
    public string? FacebookAdSetId { get; set; }

    [StringLength(100)]
    public string? FacebookAdId { get; set; }

    [StringLength(100)]
    public string? UtmSource { get; set; }

    [StringLength(100)]
    public string? UtmMedium { get; set; }

    [StringLength(200)]
    public string? UtmCampaign { get; set; }

    [StringLength(200)]
    public string? UtmContent { get; set; }

    [StringLength(200)]
    public string? UtmTerm { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public Guid CreatedById { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    [Precision(0)]
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey("CreatedById")]
    [InverseProperty("Campaigns")]
    public virtual User CreatedBy { get; set; } = null!;

    [InverseProperty("Campaign")]
    public virtual ICollection<Lead> Leads { get; set; } = new List<Lead>();
}
