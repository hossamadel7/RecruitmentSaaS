using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public enum WhatsAppConnectionMode : byte
{
    CloudApi = 1,
    Coexistence = 2
}

public enum WebhookSubscriptionStatus : byte
{
    Pending = 1,
    Subscribed = 2,
    Failed = 3
}

public partial class WhatsAppAccount
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string DisplayPhoneNumber { get; set; } = null!;

    /// <summary>Meta Cloud API phone_number_id — used to route inbound webhooks to this account.</summary>
    public string PhoneNumberId { get; set; } = null!;

    public string WabaId { get; set; } = null!;

    public byte ConnectionMode { get; set; }

    public byte WebhookSubscriptionStatus { get; set; }

    /// <summary>Business name Meta has verified for this number, when available.</summary>
    public string? VerifiedName { get; set; }

    public Guid? AssignedSalesAgentId { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual User? AssignedSalesAgent { get; set; }

    public virtual ICollection<WhatsAppConversation> WhatsAppConversations { get; set; } = new List<WhatsAppConversation>();

    public virtual ICollection<WhatsAppMessage> WhatsAppMessages { get; set; } = new List<WhatsAppMessage>();

    public virtual ICollection<WhatsAppHandoff> WhatsAppHandoffs { get; set; } = new List<WhatsAppHandoff>();
}
