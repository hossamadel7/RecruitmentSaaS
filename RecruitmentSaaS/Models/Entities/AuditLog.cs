using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class AuditLog
{
    public Guid Id { get; set; }

    public Guid? ActorId { get; set; }

    public byte ActorType { get; set; }

    public string EventType { get; set; } = null!;

    public string? EntityType { get; set; }

    public Guid? EntityId { get; set; }

    public string? OldValueJson { get; set; }

    public string? NewValueJson { get; set; }

    public string? Ipaddress { get; set; }

    public DateTime CreatedAt { get; set; }
}
