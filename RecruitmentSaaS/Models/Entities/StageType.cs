using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class StageType
{
    public Guid Id { get; set; }

    public string StageCode { get; set; } = null!;

    public string Name { get; set; } = null!;

    public byte? RequiredAction { get; set; }

    public byte? DocumentTypeRequired { get; set; }

    public string? ActionLabel { get; set; }

    public string? ActionFields { get; set; }

    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<PackageStage> PackageStages { get; set; } = new List<PackageStage>();
}
