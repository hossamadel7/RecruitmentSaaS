namespace RecruitmentSaaS.Models.Entities;

public enum AiKnowledgeKind : byte { Fact = 0, Playbook = 1 }
public enum AiKnowledgeStatus : byte { Draft = 0, Approved = 1, Retired = 2 }
public enum AiDraftStatus : byte { Generating = 0, ReviewRequired = 1, Reviewed = 2, Rejected = 3, Stale = 4, Failed = 5 }

/// <summary>Immutable content after approval. Publish a new entry to change an approved fact.</summary>
public sealed class AiSalesKnowledge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid WhatsAppAccountId { get; set; }
    public AiKnowledgeKind Kind { get; set; }
    public AiKnowledgeStatus Status { get; set; }
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string SourceReference { get; set; } = "";
    public DateTime ValidFrom { get; set; }
    public DateTime ValidUntil { get; set; }
    public Guid? CreatedById { get; set; }
    /// <summary>Owner approval recorded outside CRM; system imports never impersonate a CRM user.</summary>
    public string? OwnerApprovalReference { get; set; }
    public Guid? ApprovedById { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public long Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Staff-reviewed context supplements existing CRM fields; model output never overwrites it.</summary>
public sealed class AiCustomerMemory
{
    public Guid ConversationId { get; set; }
    public Guid TenantId { get; set; }
    public string ReviewedSummary { get; set; } = "";
    public string ReviewedDetailsJson { get; set; } = "{}";
    public Guid ReviewedById { get; set; }
    public long Version { get; set; } = 1;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AiSalesDraft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid AgentId { get; set; }
    public Guid RequestedById { get; set; }
    public string RequestKey { get; set; } = "";
    public long OwnershipVersion { get; set; }
    public long MemoryVersion { get; set; }
    public string ContextFingerprint { get; set; } = "";
    public string KnowledgeSnapshotJson { get; set; } = "[]";
    public string SourceIdsJson { get; set; } = "[]";
    public AiDraftStatus Status { get; set; }
    public string Reply { get; set; } = "";
    public string Intent { get; set; } = "";
    public string ReviewNote { get; set; } = "";
    public bool NeedsHuman { get; set; }
    public string Model { get; set; } = "";
    public string PromptVersion { get; set; } = "";
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public Guid? ReviewedById { get; set; }
    public DateTime? ReviewedAt { get; set; }
}
