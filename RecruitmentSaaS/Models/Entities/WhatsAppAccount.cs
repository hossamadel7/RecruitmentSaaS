using System;
using System.Collections.Generic;

namespace RecruitmentSaaS.Models.Entities;

public partial class WhatsAppAccount
{
    public Guid Id { get; set; }

    public string Name { get; set; } = null!;

    public string DisplayPhoneNumber { get; set; } = null!;

    /// <summary>Meta Cloud API phone_number_id — used to route inbound webhooks to this account.</summary>
    public string PhoneNumberId { get; set; } = null!;

    public string WabaId { get; set; } = null!;

    public Guid? AssignedSalesAgentId { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual User? AssignedSalesAgent { get; set; }

    public virtual ICollection<WhatsAppConversation> WhatsAppConversations { get; set; } = new List<WhatsAppConversation>();

    public virtual ICollection<WhatsAppMessage> WhatsAppMessages { get; set; } = new List<WhatsAppMessage>();

    public virtual ICollection<WhatsAppHandoff> WhatsAppHandoffs { get; set; } = new List<WhatsAppHandoff>();
}
