namespace RecruitmentSaaS.Services;

public sealed class AiSalesOptions
{
    public bool Enabled { get; set; }
    public Guid TenantId { get; set; }
    // Automatic replies require separate engine and dispatch release settings.
}

public sealed class AiSalesConflict(string message) : Exception(message);
public sealed class AiSalesValidation(string message) : Exception(message);
