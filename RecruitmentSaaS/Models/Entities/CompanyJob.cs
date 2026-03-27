using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("CompanyJobs", Schema = "demorecruitment")]
[Index("CompanyId", Name = "IX_demorecruitment_CompanyJobs_CompanyId")]
public partial class CompanyJob
{
    [Key]
    public Guid Id { get; set; }

    public Guid CompanyId { get; set; }

    [StringLength(200)]
    public string JobTitle { get; set; } = null!;

    public int RequestedCount { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    public bool IsActive { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    [ForeignKey("CompanyId")]
    [InverseProperty("CompanyJobs")]
    public virtual Company Company { get; set; } = null!;
}
