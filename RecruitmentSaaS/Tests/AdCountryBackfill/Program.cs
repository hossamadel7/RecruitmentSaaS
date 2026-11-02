using System.Text;
using System.Text.Json;
using RecruitmentSaaS.Services;

if (args.Length != 2) throw new ArgumentException("Usage: AdCountryBackfill input.json output.sql");
var input = JsonSerializer.Deserialize<AdRecord[]>(File.ReadAllText(args[0])) ?? [];
var classified = input.Select(r => (r.Id, Country: CountryRouting.Detect(r.Headline + "\n" + r.Body)))
    .Where(r => r.Country != null).DistinctBy(r => r.Id).ToList();
var sql = new StringBuilder("SET ROLE recruitapp;\nBEGIN;\n");
if (classified.Count > 0)
{
    sql.Append("WITH updated AS (UPDATE demorecruitment.\"WhatsAppConversations\" AS c SET \"AdCountry\" = v.country FROM (VALUES\n");
    sql.AppendJoin(",\n", classified.Select(r => $"('{r.Id:D}'::uuid, '{r.Country!.Replace("'", "''")}')"));
    sql.Append("\n) AS v(id, country) WHERE c.\"Id\" = v.id AND c.\"AdCountry\" IS NULL RETURNING c.\"AdCountry\") SELECT \"AdCountry\", count(*) AS updated FROM updated GROUP BY \"AdCountry\";\n");
}
sql.Append("COMMIT;\n");
File.WriteAllText(args[1], sql.ToString(), new UTF8Encoding(false));
Console.WriteLine(JsonSerializer.Serialize(new { scanned = input.Length, detected = classified.Count,
    byCountry = classified.GroupBy(r => r.Country!).ToDictionary(g => g.Key, g => g.Count()) }));

record AdRecord(Guid Id, string? Headline, string? Body);
