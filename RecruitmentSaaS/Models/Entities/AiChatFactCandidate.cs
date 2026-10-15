namespace RecruitmentSaaS.Models.Entities;

public enum AiChatFactState : byte { Pending = 0, Published = 1, Rejected = 2 }
public sealed class AiChatFactCandidate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Guid WhatsAppAccountId { get; set; }
    public Guid? SourceMessageId { get; set; }
    public string SourceFingerprint { get; set; } = "";
    public string Topic { get; set; } = "";
    public string SourceStatement { get; set; } = "";
    public string Speaker { get; set; } = "";
    public AiChatFactState State { get; set; }
    public long Version { get; set; } = 1;
    public Guid? KnowledgeId { get; set; }
    public Guid? ReviewedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
}
