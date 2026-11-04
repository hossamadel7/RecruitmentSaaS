namespace RecruitmentSaaS.Services;
public sealed class AiAdminLearningOptions
{
    public bool Enabled { get; set; }
    public bool PrivateRoutingEnabled { get; set; }
    public bool BackgroundEnabled { get; set; }
    public bool OutboundApproved { get; set; }
    public Guid[] ApprovedAccountIds { get; set; } = [];
    public string NotificationTemplate { get; set; } = "";
    public string TemplateLanguage { get; set; } = "ar";
    public int DeadlineHours { get; set; } = 24;
    public int MaxRequestsPerDay { get; set; } = 30;
}
