using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class WhatsAppContact
{
    public Guid Id { get; set; }

    public string? Name { get; set; }

    public string WhatsAppPhoneNumber { get; set; } = null!;

    /// <summary>Meta's wa_id for this contact, when it differs from the plain phone number.</summary>
    public string? WhatsAppUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public DateTime? LastSeenAt { get; set; }

    public virtual ICollection<WhatsAppConversation> WhatsAppConversations { get; set; } = new List<WhatsAppConversation>();
}
