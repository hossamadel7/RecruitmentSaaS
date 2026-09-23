using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public enum HandoffStatus : byte
{
    Generated = 1,
    Sent = 2,
    Connected = 3,
    Expired = 4,
    Cancelled = 5
}

public partial class WhatsAppHandoff
{
    public Guid Id { get; set; }

    public Guid LeadId { get; set; }

    public Guid WhatsAppAccountId { get; set; }

    public Guid AssignedSalesAgentId { get; set; }

    public string ReferenceCode { get; set; } = null!;

    public string GeneratedWhatsAppUrl { get; set; } = null!;

    public byte Status { get; set; }

    /// <summary>Set once the reference is matched to an inbound WhatsApp message and a conversation is created/linked.</summary>
    public Guid? ConversationId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? SentAt { get; set; }

    public DateTime? ConnectedAt { get; set; }

    public DateTime? ExpiredAt { get; set; }

    public virtual Lead Lead { get; set; } = null!;

    public virtual WhatsAppAccount WhatsAppAccount { get; set; } = null!;

    public virtual User AssignedSalesAgent { get; set; } = null!;

    public virtual WhatsAppConversation? Conversation { get; set; }
}
