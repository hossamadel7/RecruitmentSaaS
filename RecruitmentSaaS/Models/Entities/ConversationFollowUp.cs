using System;

namespace RecruitmentSaaS.Models.Entities;

public enum FollowUpStatus : byte
{
    Pending = 1,
    Completed = 2,
    Cancelled = 3
}

public partial class ConversationFollowUp
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    public Guid AssignedToId { get; set; }

    public Guid CreatedById { get; set; }

    public DateTime DueAt { get; set; }

    public byte Status { get; set; }

    public string? Notes { get; set; }

    public DateTime? CompletedAt { get; set; }

    public Guid? CompletedById { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual WhatsAppConversation Conversation { get; set; } = null!;

    public virtual User AssignedTo { get; set; } = null!;

    public virtual User CreatedBy { get; set; } = null!;

    public virtual User? CompletedBy { get; set; }
}
