using System.ComponentModel.DataAnnotations;

namespace RecruitmentSaaS.Models.DTOs
{
    // =============================================================================
    //  WHATSAPP SHARED INBOX DTOs
    // =============================================================================

    public class WhatsAppAccountDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string DisplayPhoneNumber { get; set; } = string.Empty;
        public string? PhoneNumberId { get; set; }
        public string? WabaId { get; set; }
        public byte ConnectionMode { get; set; }
        public byte WebhookSubscriptionStatus { get; set; }
        public string? VerifiedName { get; set; }
        public bool IsActive { get; set; }
        public int UnreadCount { get; set; }
        public DateTime? LastMessageAt { get; set; }
        public Guid? AssignedSalesAgentId { get; set; }
        public string? AssignedSalesAgentName { get; set; }
    }

    public class ConversationListItemDto
    {
        public Guid Id { get; set; }
        public string ContactName { get; set; } = string.Empty;
        public string ContactPhone { get; set; } = string.Empty;
        public Guid WhatsAppAccountId { get; set; }
        public string WhatsAppAccountName { get; set; } = string.Empty;
        public Guid? AssignedSalesAgentId { get; set; }
        public string? AssignedSalesAgentName { get; set; }
        public byte Status { get; set; }
        public byte LeadStage { get; set; }
        public int UnreadCount { get; set; }
        public string? LastMessagePreview { get; set; }
        public byte? LastMessageDirection { get; set; }
        public DateTime? LastMessageAt { get; set; }
        public string? LeadCode { get; set; }
    }

    public class ConversationDetailDto : ConversationListItemDto
    {
        public string? LostReason { get; set; }
        public Guid? LeadId { get; set; }
        public string? LeadFullName { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime OpenedAt { get; set; }
    }

    public class WhatsAppMessageDto
    {
        public Guid Id { get; set; }
        public byte Direction { get; set; }
        public byte MessageType { get; set; }
        public string? TextBody { get; set; }
        public string? MediaUrl { get; set; }
        public byte Status { get; set; }
        public string? ErrorMessage { get; set; }
        public Guid? SenderUserId { get; set; }
        public string? SenderUserName { get; set; }
        public Guid? ReplyToMessageId { get; set; }
        public DateTime WhatsAppTimestamp { get; set; }
    }

    public class SendMessageDto
    {
        [Required(ErrorMessage = "نص الرسالة مطلوب")]
        [MaxLength(4000)]
        public string Text { get; set; } = string.Empty;
    }

    public class AssignConversationDto
    {
        /// <summary>Null unassigns the conversation.</summary>
        public Guid? AgentId { get; set; }
    }

    public class AddConversationNoteDto
    {
        [Required]
        [MaxLength(1000)]
        public string Body { get; set; } = string.Empty;
    }

    public class ConversationNoteDto
    {
        public Guid Id { get; set; }
        public string Body { get; set; } = string.Empty;
        public string AuthorName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class UpdateConversationStatusDto
    {
        [Required]
        public byte Status { get; set; }
    }

    public class UpdateLeadStageDto
    {
        [Required]
        public byte LeadStage { get; set; }

        [MaxLength(200)]
        public string? LostReason { get; set; }
    }

    public class CreateFollowUpDto
    {
        [Required]
        public DateTime DueAt { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }

        public Guid? AssignedToId { get; set; }
    }

    public class ConversationFollowUpDto
    {
        public Guid Id { get; set; }
        public Guid ConversationId { get; set; }
        public string ContactName { get; set; } = string.Empty;
        public string WhatsAppAccountName { get; set; } = string.Empty;
        public Guid AssignedToId { get; set; }
        public string AssignedToName { get; set; } = string.Empty;
        public DateTime DueAt { get; set; }
        public byte Status { get; set; }
        public string? Notes { get; set; }
        public bool IsOverdue { get; set; }
    }

    public class GenerateHandoffDto
    {
        [Required]
        public Guid LeadId { get; set; }

        [Required]
        public Guid WhatsAppAccountId { get; set; }

        public Guid? AssignedSalesAgentId { get; set; }
    }

    public class WhatsAppHandoffDto
    {
        public Guid Id { get; set; }
        public string ReferenceCode { get; set; } = string.Empty;
        public string GeneratedWhatsAppUrl { get; set; } = string.Empty;
        public byte Status { get; set; }
        public Guid LeadId { get; set; }
        public string LeadFullName { get; set; } = string.Empty;
        public string WhatsAppAccountName { get; set; } = string.Empty;
        public string AssignedSalesAgentName { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? ConnectedAt { get; set; }
    }
}
