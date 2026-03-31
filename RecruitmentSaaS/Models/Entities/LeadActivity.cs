using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class LeadActivity
{
    public Guid Id { get; set; }

    public Guid LeadId { get; set; }

    public byte ActivityType { get; set; }

    public string? Description { get; set; }

    public string? Details { get; set; }

    public DateOnly? NextFollowUpDate { get; set; }

    public Guid? ReminderId { get; set; }

    public Guid? EntityId { get; set; }

    public string? EntityType { get; set; }

    public Guid? CreatedById { get; set; }

    public string? CreatedByName { get; set; }

    public byte ActorType { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual User? CreatedBy { get; set; }

    public virtual Lead Lead { get; set; } = null!;
}
