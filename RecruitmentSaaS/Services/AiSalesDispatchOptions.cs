namespace RecruitmentSaaS.Services;

public sealed class AiSalesDispatchOptions
{
    public bool Enabled { get; set; }
    public bool ReleaseApproved { get; set; }
    public bool AutomaticEnabled { get; set; }
    public Guid[] ApprovedAccountIds { get; set; } = [];
    public Guid[] ApprovedConversationIds { get; set; } = [];
    public int MaxAttemptsPerDay { get; set; } = 10;
}
