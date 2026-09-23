using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public enum MessageDirection : byte
{
    Incoming = 1,
    Outgoing = 2
}

public enum WhatsAppMessageType : byte
{
    Unknown = 0,
    Text = 1,
    Image = 2,
    Video = 3,
    Audio = 4,
    Document = 5,
    Sticker = 6,
    Location = 7,
    Contacts = 8,
    Interactive = 9
}

public enum MessageStatus : byte
{
    Received = 1,
    Queued = 2,
    Sent = 3,
    Delivered = 4,
    Read = 5,
    Failed = 6
}

public enum MessageSource : byte
{
    CloudApi = 1,
    WhatsAppBusinessApp = 2,
    Customer = 3,
    System = 4
}

public partial class WhatsAppMessage
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    public Guid WhatsAppAccountId { get; set; }

    /// <summary>Meta's wamid. Null for an outgoing message until Meta accepts the send.</summary>
    public string? WhatsAppMessageId { get; set; }

    public byte Direction { get; set; }

    public byte MessageType { get; set; }

    /// <summary>Where this message actually originated — our inbox, the salesperson's WhatsApp Business app, the customer, or a system action.</summary>
    public byte MessageSource { get; set; }

    public string? TextBody { get; set; }

    public string? MediaId { get; set; }

    public string? MediaUrl { get; set; }

    public Guid? SenderUserId { get; set; }

    public Guid? ReplyToMessageId { get; set; }

    public byte Status { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime WhatsAppTimestamp { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual WhatsAppConversation Conversation { get; set; } = null!;

    public virtual WhatsAppAccount WhatsAppAccount { get; set; } = null!;

    public virtual User? SenderUser { get; set; }

    public virtual WhatsAppMessage? ReplyToMessage { get; set; }
}
