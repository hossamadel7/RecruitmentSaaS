namespace RecruitmentSaaS.Services;

public sealed class AiSalesEngineOptions
{
    public bool DraftsEnabled { get; set; }
    public bool ExternalProcessingApproved { get; set; }
    public bool AutoReplyEnabled { get; set; }
    public bool AutoReplyAnyAssignedConversation { get; set; }
    public Guid[] AutoReplyAgentIds { get; set; } = [];
    public DateTime? AutoReplySinceUtc { get; set; }
    // Optional restrictive allowlist for an explicitly approved pilot. When
    // configured, reassignment to a different conversation does not extend consent.
    public Guid[] ApprovedConversationIds { get; set; } = [];
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
    public int MaxDraftsPerDay { get; set; } = 20;
    public int MaxOutputTokens { get; set; } = 800;

    public bool CanReplyAutomatically(Guid conversation, Guid agent) => AutoReplyEnabled &&
        AutoReplySinceUtc != null && (AutoReplyAnyAssignedConversation || ApprovedConversationIds.Contains(conversation)) && AutoReplyAgentIds.Contains(agent);
}

public sealed class AiSalesProviderException(string code) : Exception(code);
