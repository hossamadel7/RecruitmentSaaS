namespace RecruitmentSaaS.Services;

public sealed class AiSalesOptions
{
    public bool Enabled { get; set; }
    public Guid TenantId { get; set; }
    // This release intentionally exposes neither autonomous mode nor rotation switches.
}

public sealed class AiSalesConflict(string message) : Exception(message);
public sealed class AiSalesValidation(string message) : Exception(message);
