using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class CommissionTier
{
    public Guid Id { get; set; }

    public int MinDeals { get; set; }

    public int? MaxDeals { get; set; }

    public decimal AmountPerDeal { get; set; }

    public bool IsActive { get; set; }

    public Guid CreatedById { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual User CreatedBy { get; set; } = null!;
}
