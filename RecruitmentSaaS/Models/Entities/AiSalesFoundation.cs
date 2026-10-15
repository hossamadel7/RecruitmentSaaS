namespace RecruitmentSaaS.Models.Entities;

public enum AiSalesMode : byte { Disabled = 0, Shadow = 1 }
public enum ConversationControllerMode : byte { Human = 0, Intake = 1, Assisted = 2, Paused = 3 }

/// <summary>A service identity. It deliberately has no User, password, salary or session.</summary>
public sealed class AiSalesAgent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid WhatsAppAccountId { get; set; }
    public Guid? TeamManagerId { get; set; }
    public string DisplayName { get; set; } = "المساعد الافتراضي";
    public AiSalesMode Mode { get; set; }
    public bool Paused { get; set; } = true;
    public int MaxActiveConversations { get; set; } = 10;
    public long Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public partial class Lead
{
    public Guid? AssignedAiSalesAgentId { get; set; }
}

public partial class WhatsAppConversation
{
    public Guid? AssignedAiSalesAgentId { get; set; }
}

public sealed class AiLeadAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid LeadId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid AgentId { get; set; }
    public Guid? AssignedById { get; set; }
    public string? SourceReference { get; set; }
    public bool ConversationOnly { get; set; }
    public Guid? PreviousSalesId { get; set; }
    public Guid? PreviousConversationSalesId { get; set; }
    public string Reason { get; set; } = "";
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
}

public sealed class ConversationControl
{
    public Guid ConversationId { get; set; }
    public Guid TenantId { get; set; }
    public ConversationControllerMode Controller { get; set; }
    public long OwnershipVersion { get; set; } = 1;
    public Guid? LastActorId { get; set; }
    public string Reason { get; set; } = "";
    public string HandoffSummary { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AiControlAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid? ActorId { get; set; }
    public string Event { get; set; } = "";
    public string Reason { get; set; } = "";
    public long OwnershipVersion { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Persists the response to an idempotent control request, never customer text.</summary>
public sealed class AiControlRequest
{
    public Guid TenantId { get; set; }
    public string Key { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public Guid ResourceId { get; set; }
    public long Version { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class DurableWorkItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "";
    public string DeduplicationKey { get; set; } = "";
    public Guid? ConversationId { get; set; }
    public string Payload { get; set; } = "";
    public int Attempts { get; set; }
    public DateTime AvailableAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Durable reviewed text dispatch. An uncertain attempt must never be retried automatically.</summary>
public sealed class AiOutboundIntent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ConversationId { get; set; }
    public long OwnershipVersion { get; set; }
    public string OperationKey { get; set; } = "";
    public string State { get; set; } = "Blocked";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? DraftId { get; set; }
    public Guid? RequestedById { get; set; }
    public Guid? MessageId { get; set; }
    public DateTime? AttemptedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorCode { get; set; }
}

public partial class WhatsAppMessage
{
    public Guid? SenderAiSalesAgentId { get; set; }
}
