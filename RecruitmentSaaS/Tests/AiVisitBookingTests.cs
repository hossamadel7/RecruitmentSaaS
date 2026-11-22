using RecruitmentSaaS.Services;
using Xunit;

namespace RecruitmentSaaS.Tests;

public sealed class AiVisitBookingTests
{
    private static readonly DateTime Monday = new(2026, 11, 23);

    [Theory]
    [InlineData("2026-11-24", "", "2026-11-24 12:00")]      // no hour named: midday
    [InlineData("2026-11-23", "15:30", "2026-11-23 15:30")] // today, inside office hours
    [InlineData("2026-11-28", "09:00", "2026-11-28 12:00")] // before opening: midday
    [InlineData("2026-11-28", "18:00", "2026-11-28 12:00")] // at closing: midday
    public void A_confirmed_day_becomes_a_Cairo_appointment(string date, string time, string expected)
        => Assert.Equal(DateTime.Parse(expected), AiVisitBookingService.Parse(date, time, Monday));

    [Theory]
    [InlineData("")]
    [InlineData("بكرة")]
    [InlineData("2026-11-22")] // yesterday
    [InlineData("2026-11-27")] // Friday: office closed
    [InlineData("2027-01-30")] // beyond 30 days
    public void Unusable_days_are_not_booked(string date)
        => Assert.Null(AiVisitBookingService.Parse(date, "", Monday));

    [Fact]
    public void Prompt_offers_the_job_in_another_country_and_books_visits()
    {
        Assert.Contains("not available in that country but is available in the other one", OpenAiSalesDraftProvider.Instructions);
        Assert.Contains("BOOKING: set visitDate", OpenAiSalesDraftProvider.Instructions);
        Assert.DoesNotContain("أكيد هنلاقي لحضرتك فرصة مناسبة\" — without", OpenAiSalesDraftProvider.Instructions);
    }
}
