using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class CompanyJob
{
    public Guid Id { get; set; }

    public Guid CompanyId { get; set; }

    public string JobTitle { get; set; } = null!;

    public int RequestedCount { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Company Company { get; set; } = null!;
}
