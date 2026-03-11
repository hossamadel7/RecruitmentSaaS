using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("LeadFunnelHistory", Schema = "demorecruitment")]
[Index("LeadId", "CreatedAt", Name = "IX_demorecruitment_LFH_Ld", IsDescending = new[] { false, true })]
public partial class LeadFunnelHistory
{
    [Key]
    public Guid Id { get; set; }

    public Guid LeadId { get; set; }

    public byte? FromStatus { get; set; }

    public byte ToStatus { get; set; }

    public Guid ChangedById { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    [ForeignKey("ChangedById")]
    [InverseProperty("LeadFunnelHistories")]
    public virtual User ChangedBy { get; set; } = null!;

    [ForeignKey("LeadId")]
    [InverseProperty("LeadFunnelHistories")]
    public virtual Lead Lead { get; set; } = null!;
}
