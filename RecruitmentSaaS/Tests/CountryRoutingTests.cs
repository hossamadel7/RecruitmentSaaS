using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RecruitmentSaaS.Controllers;
using RecruitmentSaaS.Data;
using System.Security.Claims;

namespace RecruitmentSaaS.Tests;

public class CountryRoutingTests
{
    [Theory]
    [InlineData("فرص عمل في السُّعودية", "سعودية")]
    [InlineData("وظائف سلطنة عُمان", "عمان")]
    [InlineData("سافر تركيا", "تركيا")]
    [InlineData("فرص عمل في ارمينيا", "ارمينيا")]
    [InlineData("سافر أرمينيا", "ارمينيا")]
    [InlineData("وظائف في أَرْمِينِيَا!", "ارمينيا")]
    [InlineData("ارمينيا أو تركيا", null)]
    [InlineData("ارمينيا123", null)]
    [InlineData("سارمينيا", null)]
    [InlineData("Jobs in Saudi Arabia", "سعودية")]
    [InlineData("Jobs in Oman", "عمان")]
    [InlineData("Jobs in Türkiye", "تركيا")]
    [InlineData("Romania", null)]
    [InlineData("تركيا أو عمان", null)]
    [InlineData("", null)]
    [InlineData("تاشيرات حرة", "تأشيرات حرة")]
    [InlineData("تأشيرات حُرّة للسعودية", "تأشيرات حرة")]
    [InlineData("تأشيرات حره عمان", "تأشيرات حرة")]
    [InlineData("تأشيرات حرة أرمينيا", "تأشيرات حرة")]
    [InlineData("حرارة", null)]
    [InlineData("حرة123", null)]
    public void Detects_only_one_explicit_country(string text, string? expected) =>
        Assert.Equal(expected, CountryRouting.Detect(text));

    private static CountryRoutingRule[] Rules() => [
        new() { Country = "سعودية", Enabled = true, MinAge = 40, MaxAge = 60, FallbackCountry = "عمان" },
        new() { Country = "عمان", Enabled = true, MinAge = 20, MaxAge = 39 },
        new() { Country = "تركيا", Enabled = false, MinAge = 16, MaxAge = 80 }];

    [Theory]
    [InlineData(40, "سعودية")]
    [InlineData(60, "سعودية")]
    [InlineData(39, "عمان")]
    [InlineData(20, "عمان")]
    [InlineData(19, null)]
    [InlineData(61, null)]
    [InlineData(null, null)]
    public void Routing_respects_inclusive_ranges_and_fallback(int? age, string? expected) =>
        Assert.Equal(expected, CountryRouting.Destination(Rules(), "سعودية", age));

    [Fact]
    public void Without_the_age_only_the_ads_own_enabled_country_takes_the_lead()
    {
        var rules = Rules();
        Assert.Equal("سعودية", CountryRouting.DestinationWithoutAge(rules, "سعودية")); // no fallback to عمان
        Assert.Null(CountryRouting.DestinationWithoutAge(rules, "تركيا"));             // switched off
        Assert.Null(CountryRouting.DestinationWithoutAge(rules, "ارمينيا"));           // no rule
    }

    [Fact]
    public void Free_visas_use_their_own_age_range_and_rotation()
    {
        var rules = Rules().Append(new CountryRoutingRule
        {
            Country = CountryRouting.FreeVisas, Enabled = true, MinAge = 25, MaxAge = 50,
            FallbackCountry = "عمان"
        }).ToArray();
        Assert.Equal(CountryRouting.FreeVisas, CountryRouting.Destination(rules, CountryRouting.FreeVisas, 25));
        Assert.Equal("عمان", CountryRouting.Destination(rules, CountryRouting.FreeVisas, 24));
        Assert.Null(CountryRouting.Destination(rules, CountryRouting.FreeVisas, 51));
        Assert.NotEqual(CountryRouting.Scope("سعودية"), CountryRouting.Scope(CountryRouting.FreeVisas));
    }

    [Fact]
    public void No_fallback_or_disabled_fallback_holds_the_lead()
    {
        var rules = Rules();
        rules[0].FallbackCountry = null;
        Assert.Null(CountryRouting.Destination(rules, "سعودية", 30));
        rules[0].FallbackCountry = "عمان";
        rules[1].Enabled = false;
        Assert.Null(CountryRouting.Destination(rules, "سعودية", 30));
        Assert.Null(CountryRouting.Destination(rules, "تركيا", 30));
    }

    [Fact]
    public async Task Managers_edit_only_their_own_country_rotation()
    {
        var manager = Guid.NewGuid();
        var other = Guid.NewGuid();
        using var db = new RecruitmentCrmContext(new DbContextOptionsBuilder<RecruitmentCrmContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unused;Username=unused").Options);
        var controller = new LeadRotationController(db) { ControllerContext = new ControllerContext {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, manager.ToString()), new Claim(ClaimTypes.Role, "7")], "test")) }
        }};
        var result = await controller.ResolveScopeAsync(CountryRouting.TeamScope(other, "عمان"));
        Assert.NotNull(result);
        Assert.Equal(CountryRouting.TeamScope(manager, "عمان"), result.Value.Key);
        Assert.Equal(manager, LeadDistributor.ManagerOfScope(result.Value.Key));
        Assert.Contains("TeamMemberships", result.Value.Candidates.ToQueryString());
        var legacy = await controller.ResolveScopeAsync(LeadDistributor.TeamUnderAgeScope(manager));
        Assert.Equal(CountryRouting.TeamScope(manager, "سعودية"), legacy!.Value.Key);
        Assert.Null(await controller.ResolveScopeAsync(null, country: "invalid"));
    }

    [Fact]
    public void Country_rotation_scopes_fit_storage_and_keep_teams_separate()
    {
        var manager = Guid.NewGuid();
        foreach (var country in CountryRouting.Countries)
        {
            var scope = CountryRouting.TeamScope(manager, country);
            Assert.True(scope.Length <= 60);
            Assert.Equal(country, CountryRouting.CountryOfScope(scope));
            Assert.Equal(manager, CountryRouting.TeamOfScope(scope));
            Assert.Equal(country, CountryRouting.CountryOfScope(CountryRouting.Scope(country)));
            Assert.NotEqual(scope, CountryRouting.TeamScope(Guid.NewGuid(), country));
        }
        Assert.Null(CountryRouting.CountryOfScope("teamc:invalid:عمان"));
    }
    [Fact]
    public void Waiting_lead_query_translates_team_rules_and_fallbacks_to_sql()
    {
        using var db = new RecruitmentCrmContext(new DbContextOptionsBuilder<RecruitmentCrmContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unused;Username=unused").Options);
        var sql = CountryRouting.EligibleLeads(db).Take(200).ToQueryString();
        Assert.Contains("TeamCountryRoutingRules", sql);
        Assert.Contains("CountryRoutingRules", sql);
        Assert.Contains("ManagerId", sql);
        Assert.Contains("FallbackCountry", sql);
    }

    [Fact]
    public void Transferred_chats_are_filtered_under_destination_and_keep_original_ad_country()
    {
        var transferred = new WhatsAppConversation { Id = Guid.NewGuid(), AdCountry = "تركيا",
            Lead = new Lead { AdCountry = "تركيا", RoutedCountry = "عمان" } };
        var original = new WhatsAppConversation { Id = Guid.NewGuid(), AdCountry = "تركيا" };
        var unknown = new WhatsAppConversation { Id = Guid.NewGuid() };
        var chats = new[] { transferred, original, unknown }.AsQueryable();
        Assert.Equal(transferred.Id, Assert.Single(CountryRouting.FilterInboxCountry(chats, "عمان")).Id);
        Assert.Equal(original.Id, Assert.Single(CountryRouting.FilterInboxCountry(chats, "تركيا")).Id);
        Assert.Equal(unknown.Id, Assert.Single(CountryRouting.FilterInboxCountry(chats, null)).Id);
        Assert.Equal("تركيا", transferred.Lead.AdCountry);
    }

    [Fact]
    public void Destination_country_filter_translates_to_database_query()
    {
        using var db = new RecruitmentCrmContext(new DbContextOptionsBuilder<RecruitmentCrmContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unused;Username=unused").Options);
        var sql = CountryRouting.FilterInboxCountry(db.WhatsAppConversations, "عمان").ToQueryString();
        Assert.Contains("RoutedCountry", sql);
        Assert.Contains("AdCountry", sql);
        Assert.Contains("COALESCE", sql);
    }
}
