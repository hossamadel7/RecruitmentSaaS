using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class FollowUpReminder
{
    public Guid Id { get; set; }

    public Guid LeadId { get; set; }

    public Guid? ActivityId { get; set; }

    public Guid AssignedToId { get; set; }

    public Guid CreatedById { get; set; }

    public DateOnly ReminderDate { get; set; }

    public byte Status { get; set; }

    public DateOnly? SnoozedUntil { get; set; }

    public int SnoozeCount { get; set; }

    public Guid? NotificationId { get; set; }

    public DateTime? DismissedAt { get; set; }

    public Guid? DismissedById { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual User AssignedTo { get; set; } = null!;

    public virtual User CreatedBy { get; set; } = null!;

    public virtual Lead Lead { get; set; } = null!;
}
