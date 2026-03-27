using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RecruitmentSaaS.Models.Entities;

[Table("FollowUpReminders", Schema = "demorecruitment")]
[Index("AssignedToId", "Status", "ReminderDate", Name = "IX_demorecruitment_FUR_At")]
[Index("ReminderDate", "Status", Name = "IX_demorecruitment_FUR_Dt")]
public partial class FollowUpReminder
{
    [Key]
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

    [Precision(0)]
    public DateTime? DismissedAt { get; set; }

    public Guid? DismissedById { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    [Precision(0)]
    public DateTime CreatedAt { get; set; }

    [Precision(0)]
    public DateTime? UpdatedAt { get; set; }

    public Guid? CandidateId { get; set; }

    [ForeignKey("AssignedToId")]
    [InverseProperty("FollowUpReminderAssignedTos")]
    public virtual User AssignedTo { get; set; } = null!;

    [ForeignKey("CandidateId")]
    [InverseProperty("FollowUpReminders")]
    public virtual Candidate? Candidate { get; set; }

    [ForeignKey("CreatedById")]
    [InverseProperty("FollowUpReminderCreatedBies")]
    public virtual User CreatedBy { get; set; } = null!;

    [ForeignKey("LeadId")]
    [InverseProperty("FollowUpReminders")]
    public virtual Lead Lead { get; set; } = null!;
}
