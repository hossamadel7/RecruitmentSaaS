using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("LeadActivities", Schema = "demorecruitment")]
[Index("LeadId", "CreatedAt", Name = "IX_demorecruitment_LA_Ld", IsDescending = new[] { false, true })]
public partial class LeadActivity
{
    [Key]
    public Guid Id { get; set; }

    public Guid LeadId { get; set; }

    public byte ActivityType { get; set; }

    [StringLength(1000)]
    public string? Description { get; set; }

    public string? Details { get; set; }

    public DateOnly? NextFollowUpDate { get; set; }

    public Guid? ReminderId { get; set; }

    public Guid? EntityId { get; set; }

    [StringLength(50)]
    public string? EntityType { get; set; }

    public Guid? CreatedById { get; set; }

    [StringLength(200)]
    public string? CreatedByName { get; set; }

    public byte ActorType { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    [ForeignKey("CreatedById")]
    [InverseProperty("LeadActivities")]
    public virtual User? CreatedBy { get; set; }

    [ForeignKey("LeadId")]
    [InverseProperty("LeadActivities")]
    public virtual Lead Lead { get; set; } = null!;
}
