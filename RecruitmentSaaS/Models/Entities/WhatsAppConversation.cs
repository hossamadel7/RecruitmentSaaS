using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

/// <summary>Conversation workflow state — separate from LeadStage, which tracks sales-pipeline progress.</summary>
public enum ConversationStatus : byte
{
    New = 1,
    Open = 2,
    WaitingForCustomer = 3,
    FollowUp = 4,
    Closed = 5
}

public enum LeadStage : byte
{
    New = 1,
    Contacted = 2,
    Interested = 3,
    FollowUp = 4,
    Qualified = 5,
    Won = 6,
    Lost = 7
}

public partial class WhatsAppConversation
{
    public Guid Id { get; set; }

    public Guid ContactId { get; set; }

    public Guid WhatsAppAccountId { get; set; }

    public Guid? AssignedSalesAgentId { get; set; }

    /// <summary>Optional bridge to the existing recruitment Leads table (Messenger → WhatsApp handoff).</summary>
    public Guid? LeadId { get; set; }

    public byte Status { get; set; }

    public byte LeadStage { get; set; }

    public string? LostReason { get; set; }

    public int UnreadCount { get; set; }

    public DateTime OpenedAt { get; set; }

    public DateTime? LastMessageAt { get; set; }

    public DateTime? ClosedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual WhatsAppContact Contact { get; set; } = null!;

    public virtual WhatsAppAccount WhatsAppAccount { get; set; } = null!;

    public virtual User? AssignedSalesAgent { get; set; }

    public virtual Lead? Lead { get; set; }

    public virtual ICollection<WhatsAppMessage> WhatsAppMessages { get; set; } = new List<WhatsAppMessage>();

    public virtual ICollection<ConversationNote> ConversationNotes { get; set; } = new List<ConversationNote>();

    public virtual ICollection<ConversationFollowUp> ConversationFollowUps { get; set; } = new List<ConversationFollowUp>();

    public virtual ICollection<WhatsAppHandoff> WhatsAppHandoffs { get; set; } = new List<WhatsAppHandoff>();
}
