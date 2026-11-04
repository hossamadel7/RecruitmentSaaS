namespace RecruitmentSaaS.Models.Entities;

public enum AiLearningState : byte { Pending, Processing, Completed, Failed, Stale }
public enum AiKnowledgeCategory : byte { Business, Faq, Strategy, Objection, Example, Pricing, Workflow, Escalation }
public enum AiFeedbackKind : byte { Helpful, Incorrect, HumanEdited, Successful, Failed, EscalationRequired }

public sealed class AiLearningAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid WhatsAppAccountId { get; set; }
    public bool Paused { get; set; } = true;
    public bool IncludeOpenHistory { get; set; }
    public Guid Cursor { get; set; }
    public Guid[] ExcludedConversationIds { get; set; } = [];
    public DateTime? LastScanAt { get; set; }
    public long Version { get; set; } = 1;
}

public sealed class AiConversationAnalysis
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid WhatsAppAccountId { get; set; }
    public Guid ConversationId { get; set; }
    public string Fingerprint { get; set; } = "";
    public AiLearningState State { get; set; }
    public int Attempts { get; set; }
    public DateTime AvailableAt { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string ErrorCode { get; set; } = "";
    public string InferredStage { get; set; } = "NEW_LEAD";
    public string Language { get; set; } = "unknown";
    public string VerifiedOutcome { get; set; } = "UNKNOWN";
    public string OutcomeEvidence { get; set; } = "";
    public string Cohort { get; set; } = "";
    public Guid? SalespersonId { get; set; }
    public bool IsEvaluationHoldout { get; set; }
    public string InsightsJson { get; set; } = "{}";
    public long Version { get; set; } = 1;
}

/// <summary>Metadata/index for the existing knowledge row. Its existing approval and expiry remain authoritative.</summary>
public sealed class AiKnowledgeIndex
{
    public Guid KnowledgeId { get; set; }
    public Guid TenantId { get; set; }
    public Guid WhatsAppAccountId { get; set; }
    public Guid? AnalysisId { get; set; }
    public AiKnowledgeCategory Category { get; set; }
    public double Confidence { get; set; }
    public string ContentHash { get; set; } = "";
    public string SourceFingerprint { get; set; } = "";
    public long KnowledgeVersion { get; set; }
    public string EmbeddingModel { get; set; } = "";
    public float[] Embedding { get; set; } = [];
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AiSalesFeedback
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid DraftId { get; set; }
    public Guid ActorId { get; set; }
    public AiFeedbackKind Kind { get; set; }
    public string Note { get; set; } = "";
    public string EditedReply { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AiLearningUsage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public string Kind { get; set; } = "";
    public string Model { get; set; } = "";
    public string Status { get; set; } = "Reserved";
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AiEvaluationCase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid WhatsAppAccountId { get; set; }
    public Guid? AnalysisId { get; set; }
    public string Dataset { get; set; } = "baseline-v1";
    public string Scenario { get; set; } = "";
    public string Expected { get; set; } = "";
    public string ContextJson { get; set; } = "{}";
    public bool PrivacyApproved { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AiEvaluationResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid CaseId { get; set; }
    public Guid RunId { get; set; }
    public Guid ActorId { get; set; }
    public string Configuration { get; set; } = "";
    public string Baseline { get; set; } = "";
    public string Reply { get; set; } = "";
    public string ReviewJson { get; set; } = "{}";
    public bool? Passed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
