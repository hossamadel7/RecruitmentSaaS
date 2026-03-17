using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("CommissionTiers", Schema = "demorecruitment")]
[Index("IsActive", "MinDeals", Name = "IX_demorecruitment_CT_Active")]
public partial class CommissionTier
{
    [Key]
    public Guid Id { get; set; }

    public int MinDeals { get; set; }

    public int? MaxDeals { get; set; }

    [Column(TypeName = "decimal(18, 2)")]
    public decimal AmountPerDeal { get; set; }

    public bool IsActive { get; set; }

    public Guid CreatedById { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    [Precision(0)]
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey("CreatedById")]
    [InverseProperty("CommissionTiers")]
    public virtual User CreatedBy { get; set; } = null!;
}
