using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class Notification
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public byte Type { get; set; }

    public string Title { get; set; } = null!;

    public string? Body { get; set; }

    public Guid? EntityId { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? Link { get; set; }

    public DateTime? ReadAt { get; set; }

    public virtual User User { get; set; } = null!;
}
