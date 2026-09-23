using System;

namespace RecruitmentSaaS.Models.Entities;

/// <summary>Internal note on a WhatsApp conversation. Never sent to the customer.</summary>
public partial class ConversationNote
{
    public Guid Id { get; set; }

    public Guid ConversationId { get; set; }

    public Guid AuthorId { get; set; }

    public string Body { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual WhatsAppConversation Conversation { get; set; } = null!;

    public virtual User Author { get; set; } = null!;
}
