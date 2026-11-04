namespace RecruitmentSaaS.Models.Entities;

public enum AiAdminRequestState : byte { Queued, AwaitingAdmin, AnswerReceived, Validated, ReviewReady, Escalated, Cancelled }
public enum AiAdminAnswerScope : byte { Incomplete, CustomerOnly, General, Temporary, Correction }

/// <summary>Private channel, deliberately outside customer conversations and conversion reports.</summary>
public sealed class AiAdminChannel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid WhatsAppAccountId { get; set; }
    public Guid AdminUserId { get; set; }
    public string Phone { get; set; } = "";
    public string VerificationHash { get; set; } = "";
    public DateTime VerificationExpiresAt { get; set; }
    public int VerificationAttempts { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public bool Paused { get; set; } = true;
    public DateTime? LastInboundAt { get; set; }
    public DateTime? LastNotificationAt { get; set; }
    public long Version { get; set; } = 1;
}

public sealed class AiAdminMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChannelId { get; set; }
    public Guid? RequestId { get; set; }
    public string? WhatsAppMessageId { get; set; }
    public string OperationKey { get; set; } = "";
    public bool Incoming { get; set; }
    public bool IsTemplate { get; set; }
    public string Text { get; set; } = "";
    public string State { get; set; } = "Queued";
    public string? ErrorCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeliveredAt { get; set; }
}

public sealed class AiAdminClarification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ChannelId { get; set; }
    public Guid AgentId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid CustomerMessageId { get; set; }
    public long OwnershipVersion { get; set; }
    public string Code { get; set; } = "AI-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
    public string Question { get; set; } = "";
    public string Answer { get; set; } = "";
    public AiAdminAnswerScope AnswerScope { get; set; }
    public AiAdminRequestState State { get; set; }
    public Guid? AnswerMessageId { get; set; }
    public Guid? ValidatedById { get; set; }
    public Guid? KnowledgeId { get; set; }
    public Guid? ReplyDraftId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime Deadline { get; set; }
    public DateTime AnswerValidUntil { get; set; }
    public long Version { get; set; } = 1;
    public string? ErrorCode { get; set; }
}
