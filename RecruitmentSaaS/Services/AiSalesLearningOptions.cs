namespace RecruitmentSaaS.Services;
public sealed class AiSalesLearningOptions
{
    public bool Enabled { get; set; }
    public bool BackgroundEnabled { get; set; }
    public bool ExternalProcessingApproved { get; set; }
    public bool PrivacyApproved { get; set; }
    public bool SemanticRetrievalEnabled { get; set; }
    public Guid[] ApprovedAccountIds { get; set; } = [];
    public int ScanBatchSize { get; set; } = 25;
    public int MaxMessagesPerConversation { get; set; } = 5000;
    public int MaxCallsPerDay { get; set; } = 100;
    public int PollSeconds { get; set; } = 60;
    public int RetentionDays { get; set; } = 90;
    public int MaxIndexedKnowledge { get; set; } = 1000;
    public int HoldoutPercentage { get; set; } = 10;
    public int EmbeddingDimensions { get; set; } = 512;
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public decimal InputCostPerMillion { get; set; }
    public decimal OutputCostPerMillion { get; set; }
    public decimal EmbeddingCostPerMillion { get; set; }
}
