using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("AuditLogs", Schema = "demorecruitment")]
[Index("CreatedAt", Name = "IX_demorecruitment_Aud_Dt", AllDescending = true)]
[Index("EntityId", "CreatedAt", Name = "IX_demorecruitment_Aud_En", IsDescending = new[] { false, true })]
[Index("EventType", "CreatedAt", Name = "IX_demorecruitment_Aud_Ev", IsDescending = new[] { false, true })]
public partial class AuditLog
{
    [Key]
    public Guid Id { get; set; }

    public Guid? ActorId { get; set; }

    public byte ActorType { get; set; }

    [StringLength(100)]
    public string EventType { get; set; } = null!;

    [StringLength(100)]
    public string? EntityType { get; set; }

    public Guid? EntityId { get; set; }

    public string? OldValueJson { get; set; }

    public string? NewValueJson { get; set; }

    [Column("IPAddress")]
    [StringLength(50)]
    public string? Ipaddress { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }
}
