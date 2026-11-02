namespace RecruitmentSaaS.Models.Entities;

public class CountryRoutingRule
{
    public string Country { get; set; } = "";
    public bool Enabled { get; set; }
    public int MinAge { get; set; } = 18;
    public int MaxAge { get; set; } = 80;
    public string? FallbackCountry { get; set; }
}
