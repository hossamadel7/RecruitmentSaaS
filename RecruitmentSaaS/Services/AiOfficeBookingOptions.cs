namespace RecruitmentSaaS.Services;
public sealed class AiOfficeBookingOptions
{
    public bool Enabled { get; set; }
    public int MaxDaysAhead { get; set; } = 30;
}
