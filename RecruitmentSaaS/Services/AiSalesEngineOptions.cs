namespace RecruitmentSaaS.Services;

public sealed class AiSalesEngineOptions
{
    public bool DraftsEnabled { get; set; }
    public bool ExternalProcessingApproved { get; set; }
    // Optional restrictive allowlist for an explicitly approved pilot. When
    // configured, reassignment to a different conversation does not extend consent.
    public Guid[] ApprovedConversationIds { get; set; } = [];
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
    public int MaxDraftsPerDay { get; set; } = 20;
    public int MaxOutputTokens { get; set; } = 800;
}

public sealed class AiSalesProviderException(string code) : Exception(code);
