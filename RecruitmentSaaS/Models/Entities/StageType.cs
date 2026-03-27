using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("StageTypes", Schema = "demorecruitment")]
[Index("StageCode", Name = "UQ_DR_ST_Code", IsUnique = true)]
public partial class StageType
{
    [Key]
    public Guid Id { get; set; }

    [StringLength(50)]
    public string StageCode { get; set; } = null!;

    [StringLength(200)]
    public string Name { get; set; } = null!;

    public byte? RequiredAction { get; set; }

    public byte? DocumentTypeRequired { get; set; }

    [StringLength(200)]
    public string? ActionLabel { get; set; }

    [StringLength(500)]
    public string? ActionFields { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public int SortOrder { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime CreatedAt { get; set; }

    [InverseProperty("StageType")]
    public virtual ICollection<PackageStage> PackageStages { get; set; } = new List<PackageStage>();
}
