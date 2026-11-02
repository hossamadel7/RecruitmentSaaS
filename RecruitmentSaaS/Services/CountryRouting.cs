using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Services;

public static class CountryRouting
{
    public const string FreeVisas = "تأشيرات حرة";
    public static readonly string[] Countries = ["سعودية", "عمان", "تركيا", "ارمينيا", FreeVisas];
    public static string? DisplayName(string? country) => country == "ارمينيا" ? "أرمينا" : country;
    internal static IQueryable<WhatsAppConversation> FilterInboxCountry(IQueryable<WhatsAppConversation> query, string? country) =>
        query.Where(c => ((c.Lead != null ? c.Lead.RoutedCountry ?? c.Lead.AdCountry : null) ?? c.AdCountry) == country);

    public static string Scope(string country) => "country:" + country;
    public static string TeamScope(Guid managerId, string country) => "teamc:" + managerId.ToString("N") + ":" + country;
    public static string? CountryOfScope(string? scope)
    {
        var country = scope?.StartsWith("country:") == true ? scope[8..]
            : TeamOfScope(scope) != null ? scope![39..] : null;
        return Countries.Contains(country) ? country : null;
    }
    public static Guid? TeamOfScope(string? scope) => scope?.StartsWith("teamc:") == true && scope.Length > 39 &&
        scope[38] == ':' && Guid.TryParseExact(scope.Substring(6, 32), "N", out var id) ? id : null;

    public static async Task<List<CountryRoutingRule>> RulesAsync(RecruitmentCrmContext db, Guid? managerId, CancellationToken ct = default) =>
        managerId == null ? await db.CountryRoutingRules.AsNoTracking().ToListAsync(ct)
        : await db.TeamCountryRoutingRules.AsNoTracking().Where(r => r.ManagerId == managerId)
            .Select(r => new CountryRoutingRule { Country = r.Country, Enabled = r.Enabled,
                MinAge = r.MinAge, MaxAge = r.MaxAge, FallbackCountry = r.FallbackCountry }).ToListAsync(ct);

    private static string Normalize(string text) => new(text.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && c != 'ـ')
        .Select(c => c is 'أ' or 'إ' or 'آ' ? 'ا' : c == 'ة' ? 'ه' : char.ToLowerInvariant(c)).ToArray());

    public static string? Detect(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var normalized = Normalize(text);
        // This ad category takes precedence even if the ad also names a country.
        if (Regex.IsMatch(normalized, @"(?<![\p{L}\p{N}])حره(?![\p{L}\p{N}])"))
            return FreeVisas;
        var aliases = new[] { new[] { "سعودية", "السعودية", "سعوديه", "السعوديه", "saudi", "ksa" },
            new[] { "عمان", "oman" }, new[] { "تركيا", "تركيه", "turkey", "turkiye", "türkiye" },
            new[] { "ارمينيا", "أرمينيا" } };
        var matches = Countries.Take(aliases.Length).Where((c, i) => aliases[i].Any(a => Regex.IsMatch(normalized,
            @"(?<![\p{L}\p{N}])" + Regex.Escape(Normalize(a)) + @"(?![\p{L}\p{N}])"))).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    public static string? Destination(IEnumerable<CountryRoutingRule> rules, string country, int? age)
    {
        var rule = rules.FirstOrDefault(r => r.Country == country && r.Enabled);
        if (rule == null || age == null) return null;
        if (age >= rule.MinAge && age <= rule.MaxAge) return country;
        var fallback = rules.FirstOrDefault(r => r.Country == rule.FallbackCountry && r.Enabled);
        return fallback != null && age >= fallback.MinAge && age <= fallback.MaxAge ? fallback.Country : null;
    }

    internal static IQueryable<Lead> EligibleLeads(RecruitmentCrmContext db) =>
        db.Leads
                .Where(l => l.AdCountry == null || (l.TeamManagerId == null && db.CountryRoutingRules.Any(r =>
                    r.Enabled && r.Country == l.AdCountry && l.Age != null &&
                    ((l.Age >= r.MinAge && l.Age <= r.MaxAge) ||
                     ((l.Age < r.MinAge || l.Age > r.MaxAge) && db.CountryRoutingRules.Any(f =>
                         f.Country == r.FallbackCountry && f.Enabled && l.Age >= f.MinAge && l.Age <= f.MaxAge)))))
                    || (l.TeamManagerId != null && db.TeamCountryRoutingRules.Any(r =>
                        r.ManagerId == l.TeamManagerId && r.Enabled && r.Country == l.AdCountry && l.Age != null &&
                        ((l.Age >= r.MinAge && l.Age <= r.MaxAge) ||
                         ((l.Age < r.MinAge || l.Age > r.MaxAge) && db.TeamCountryRoutingRules.Any(f =>
                             f.ManagerId == l.TeamManagerId && f.Country == r.FallbackCountry && f.Enabled && l.Age >= f.MinAge && l.Age <= f.MaxAge))))));

    public static async Task<Guid?> AssignAsync(RecruitmentCrmContext db, Lead lead, CancellationToken ct)
    {
        var rules = await RulesAsync(db, lead.TeamManagerId, ct);
        lead.RoutedCountry = Destination(rules, lead.AdCountry!, lead.Age);
        if (lead.RoutedCountry == null) return null;
        var manager = lead.TeamManagerId;
        var candidates = manager == null ? db.Users : db.Users.Where(u => u.ManagerId == manager || u.TeamMemberships.Any(m => m.ManagerId == manager));
        return await LeadDistributor.NextTeleSalesAsync(db, candidates,
            manager == null ? Scope(lead.RoutedCountry) : TeamScope(manager.Value, lead.RoutedCountry), ct);
    }
}
