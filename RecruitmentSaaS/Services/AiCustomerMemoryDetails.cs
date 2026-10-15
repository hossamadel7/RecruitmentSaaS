using System.Text.Json;

namespace RecruitmentSaaS.Services;

public enum AiPassportReadiness : byte { Unknown = 0, Available = 1, Applying = 2, Renewing = 3 }

/// <summary>Reviewed customer-specific preferences; never evidence of company terms or a saved booking.</summary>
public sealed record AiCustomerMemoryDetails(AiPassportReadiness PassportReadiness = AiPassportReadiness.Unknown,
    string? PreferredWork = "", string? MainObjection = "", string? NextStep = "", string? VisitPreference = "")
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public AiCustomerMemoryDetails Normalize()
    {
        if (!Enum.IsDefined(PassportReadiness)) throw new AiSalesValidation("Select a known passport readiness value.");
        string Field(string? text, int limit)
        {
            var value = (text ?? "").Trim();
            if (value.Length > limit) throw new AiSalesValidation("Customer memory field exceeds its limit.");
            return value;
        }
        return this with { PreferredWork = Field(PreferredWork, 200), MainObjection = Field(MainObjection, 500),
            NextStep = Field(NextStep, 500), VisitPreference = Field(VisitPreference, 250) };
    }

    public bool HasContent => PassportReadiness != AiPassportReadiness.Unknown ||
        new[] { PreferredWork, MainObjection, NextStep, VisitPreference }.Any(s => !string.IsNullOrWhiteSpace(s));

    public string ToJson() => JsonSerializer.Serialize(Normalize(), JsonOptions);

    public static AiCustomerMemoryDetails Read(string json)
    {
        try { return (JsonSerializer.Deserialize<AiCustomerMemoryDetails>(json, JsonOptions)
            ?? throw new AiSalesValidation("Customer memory details are invalid.")).Normalize(); }
        catch (JsonException) { throw new AiSalesValidation("Customer memory details are invalid; staff review is required."); }
    }

    public AiCustomerMemoryDetails ForModel() => this with
    {
        PreferredWork = AiSalesContextService.Redact(PreferredWork ?? ""),
        MainObjection = AiSalesContextService.Redact(MainObjection ?? ""),
        NextStep = AiSalesContextService.Redact(NextStep ?? ""),
        VisitPreference = AiSalesContextService.Redact(VisitPreference ?? "")
    };
}
