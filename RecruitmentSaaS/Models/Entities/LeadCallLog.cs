using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class LeadCallLog
{
    public Guid Id { get; set; }

    public Guid LeadId { get; set; }

    public Guid CalledById { get; set; }

    public byte Channel { get; set; }

    public byte Outcome { get; set; }

    public string? Note { get; set; }

    public DateTime CalledAt { get; set; }

    public virtual User CalledBy { get; set; } = null!;

    public virtual Lead Lead { get; set; } = null!;
}
