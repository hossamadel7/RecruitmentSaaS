using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class LeadFunnelHistory
{
    public Guid Id { get; set; }

    public Guid LeadId { get; set; }

    public byte? FromStatus { get; set; }

    public byte ToStatus { get; set; }

    public Guid ChangedById { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User ChangedBy { get; set; } = null!;

    public virtual Lead Lead { get; set; } = null!;
}
