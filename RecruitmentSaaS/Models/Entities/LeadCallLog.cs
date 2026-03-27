using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("LeadCallLog", Schema = "demorecruitment")]
[Index("LeadId", "CalledAt", Name = "IX_demorecruitment_LCL_Ld", IsDescending = new[] { false, true })]
public partial class LeadCallLog
{
    [Key]
    public Guid Id { get; set; }

    public Guid LeadId { get; set; }

    public Guid CalledById { get; set; }

    public byte Channel { get; set; }

    public byte Outcome { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }

    [Precision(0)]
    public DateTime CalledAt { get; set; }

    [ForeignKey("CalledById")]
    [InverseProperty("LeadCallLogs")]
    public virtual User CalledBy { get; set; } = null!;

    [ForeignKey("LeadId")]
    [InverseProperty("LeadCallLogs")]
    public virtual Lead Lead { get; set; } = null!;
}
