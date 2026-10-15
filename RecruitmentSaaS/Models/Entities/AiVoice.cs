namespace RecruitmentSaaS.Models.Entities;

public enum AiVoiceStatus : byte { Pending = 0, Processing = 1, ReadyForReview = 2, Reviewed = 3, Failed = 4, Stale = 5 }
public enum AiVoicePartStatus : byte { Pending = 0, Processing = 1, Completed = 2, Failed = 3 }

public sealed class AiVoiceTurn
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid AgentId { get; set; }
    public long OwnershipVersion { get; set; }
    public AiVoiceStatus Status { get; set; }
    public long Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastMessageAt { get; set; }
    public DateTime AvailableAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string Transcript { get; set; } = "";
    public string ReviewedText { get; set; } = "";
    public bool NeedsClarification { get; set; }
    public double? LowestTokenProbability { get; set; }
    public string? ErrorCode { get; set; }
    public Guid? RequestedById { get; set; }
    public Guid? ReviewedById { get; set; }
    public DateTime? ReviewedAt { get; set; }
}

public sealed class AiVoicePart
{
    public Guid MessageId { get; set; }
    public Guid TurnId { get; set; }
    public string MediaId { get; set; } = "";
    public string MediaSha256 { get; set; } = "";
    public string ContentType { get; set; } = "";
    public int? ByteCount { get; set; }
    public AiVoicePartStatus Status { get; set; }
    public string Transcript { get; set; } = "";
    public double? LowestTokenProbability { get; set; }
    public string Model { get; set; } = "";
    public double? ReportedSeconds { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public int AttemptsOnLastDay { get; set; }
    public string? ErrorCode { get; set; }
}
