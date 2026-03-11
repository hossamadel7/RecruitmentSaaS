using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("Notifications", Schema = "demorecruitment")]
[Index("UserId", "IsRead", "CreatedAt", Name = "IX_demorecruitment_Not_Us", IsDescending = new[] { false, false, true })]
public partial class Notification
{
    [Key]
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public byte Type { get; set; }

    [StringLength(200)]
    public string Title { get; set; } = null!;

    [StringLength(500)]
    public string? Body { get; set; }

    public Guid? EntityId { get; set; }

    public bool IsRead { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    [ForeignKey("UserId")]
    [InverseProperty("Notifications")]
    public virtual User User { get; set; } = null!;
}
