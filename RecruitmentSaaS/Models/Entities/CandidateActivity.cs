using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("CandidateActivities", Schema = "demorecruitment")]
[Index("CandidateId", "CreatedAt", Name = "IX_demo_CA_CandId", IsDescending = new[] { false, true })]
public partial class CandidateActivity
{
    [Key]
    public Guid Id { get; set; }

    public Guid CandidateId { get; set; }

    public byte ActivityType { get; set; }

    [StringLength(1000)]
    public string? Description { get; set; }

    public string? Details { get; set; }

    public Guid? CreatedById { get; set; }

    [StringLength(200)]
    public string? CreatedByName { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    [ForeignKey("CandidateId")]
    [InverseProperty("CandidateActivities")]
    public virtual Candidate Candidate { get; set; } = null!;

    [ForeignKey("CreatedById")]
    [InverseProperty("CandidateActivities")]
    public virtual User? CreatedBy { get; set; }
}
