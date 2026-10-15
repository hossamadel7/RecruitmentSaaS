using Microsoft.Extensions.Options;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Explicit local trial only: no app host, database, CRM export, transport, worker or production config.
if (args.Length is < 3 or > 4 || !Path.IsPathFullyQualified(args[0]) || !Path.IsPathFullyQualified(args[1]))
    throw new ArgumentException("Usage: RealModelRunner <absolute workspace> <absolute output directory> <model> [comma-separated case IDs]");
var root = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
if (output.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("Trial output must remain outside the repository.");
var key = Environment.GetEnvironmentVariable("AI_SALES_TRIAL_API_KEY");
if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Configure AI_SALES_TRIAL_API_KEY securely in the process environment.");
Environment.SetEnvironmentVariable("AI_SALES_TRIAL_API_KEY", null);
var packPath = Path.Combine(root, "docs", "knowledge", "turkey-owner-knowledge.json");
var checksPath = Path.Combine(root, "docs", "knowledge", "turkey-response-checks.json");
using var pack = JsonDocument.Parse(await File.ReadAllTextAsync(packPath));
using var checks = JsonDocument.Parse(await File.ReadAllTextAsync(checksPath));
if (!checks.RootElement.GetProperty("syntheticCustomerInputs").GetBoolean())
    throw new ArgumentException("This runner accepts fictional customer inputs only.");
var scenarios = checks.RootElement.GetProperty("cases").EnumerateArray().ToArray();
if (args.Length == 4)
{
    var selected = args[3].Split(',').ToHashSet(StringComparer.Ordinal);
    if (selected.Any(id => !scenarios.Any(c => c.GetProperty("id").GetString() == id)))
        throw new ArgumentException("Unknown case ID.");
    scenarios = scenarios.Where(c => selected.Contains(c.GetProperty("id").GetString()!)).ToArray();
}
if (scenarios.Length is < 1 or > 20) throw new ArgumentException("Trial is bounded to 1–20 requests, without retries.");
var facts = pack.RootElement.GetProperty("entries").EnumerateArray().Select(e =>
    new AiContextFact(Guid.NewGuid(), 0, (AiKnowledgeKind)e.GetProperty("kind").GetByte(),
        e.GetProperty("title").GetString()!, e.GetProperty("content").GetString()!, DateTime.UtcNow.AddDays(1))).ToArray();
var factIds = facts.Where(f => f.Kind == AiKnowledgeKind.Fact).Select(f => f.Id).ToHashSet();
using var diagnostic = new TrialDiagnosticHandler();
using var http = new HttpClient(diagnostic);
var provider = new OpenAiSalesDraftProvider(http, Options.Create(new AiSalesEngineOptions
{
    DraftsEnabled = true, ExternalProcessingApproved = true, ApiKey = key, Model = args[2], MaxOutputTokens = 800
}));
Directory.CreateDirectory(output);
var rows = new List<object>();
var preview = new StringBuilder("# Real-model trial — fictional applicants\n\nHuman review pending. No customer history, messages sent, bookings, database changes or model fine-tuning.\n\n");
var attempted = 0; var completed = 0; string? failure = null;
foreach (var c in scenarios)
{
    var id = c.GetProperty("id").GetString()!;
    var question = c.GetProperty("customer").GetString()!;
    var history = new List<AiContextMessage>();
    if (c.TryGetProperty("history", out var prior))
        foreach (var m in prior.EnumerateArray())
            history.Add(new(Guid.NewGuid(), m.GetProperty("speaker").GetString()!, "Text", m.GetProperty("text").GetString()!, DateTime.UtcNow.AddMinutes(-1)));
    history.Add(new(Guid.NewGuid(), "customer", "Text", question, DateTime.UtcNow));
    var context = new AiSalesContext(new(null, null, null, null, null, "", 0), history.ToArray(), facts);
    attempted++;
    try
    {
        var local = AiSalesReplyPolicy.Create(context);
        var draft = local ?? await provider.GenerateAsync(context, CancellationToken.None);
        completed++;
        var sourcesValid = draft.SourceIds.All(factIds.Contains);
        rows.Add(new { id, customer = question, history = history.ToArray(), draft, sourcesValid, path = local == null ? "provider" : "local-policy",
            expected = c.Clone(), semanticAssessment = "pending_human_review" });
        preview.AppendLine($"## {id}\n\n**Applicant:** {question}\n\n**Draft:** {draft.Reply}\n\nIntent: {draft.Intent}; needs human: {draft.NeedsHuman}; valid fact references: {sourcesValid}.\n\nReviewer note: {draft.ReviewNote}\n");
        Console.WriteLine($"{id}: {(local == null ? "provider" : "local-policy")}; human assessment pending; fact references valid={sourcesValid}");
    }
    catch (AiSalesProviderException ex) { failure = ex.Message; }
    catch (OperationCanceledException) { failure = "ProviderTimeout"; }
    catch (HttpRequestException) { failure = "ProviderNetworkFailure"; }
    catch (Exception) { failure = "TrialUnexpectedFailure"; }
    var report = new
    {
        generatedAtUtc = DateTime.UtcNow, modelRequested = args[2], promptVersion = OpenAiSalesDraftProvider.PromptVersion,
        knowledgeSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(packPath))),
        scenariosSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(checksPath))),
        fictionalCustomerInputs = true, containsOwnerBusinessClaims = true, realCustomerHistoryUploaded = false,
        attempted, completed, planned = scenarios.Length, failure, providerErrorCode = diagnostic.ErrorCode, humanScores = (object?)null,
        semanticAssessment = "pending_human_review", livePilotQualified = false, modelFineTuned = false,
        knowledge = facts, results = rows
    };
    await File.WriteAllTextAsync(Path.Combine(output, "real-model-trial.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    await File.WriteAllTextAsync(Path.Combine(output, "reply-review.md"), preview.ToString());
    if (failure != null) { Console.WriteLine($"Stopped after {id}: {failure}; provider code={diagnostic.ErrorCode ?? "unavailable"}; no retry performed."); break; }
}
Console.WriteLine($"Generated {completed}/{scenarios.Length} drafts. Report: {Path.Combine(output, "real-model-trial.json")}");
Environment.ExitCode = failure == null ? 0 : 1;

// Capture only recognized operational codes. Never record the provider's free-text error or headers.
sealed class TrialDiagnosticHandler : DelegatingHandler
{
    public string? ErrorCode { get; private set; }
    public TrialDiagnosticHandler() : base(new HttpClientHandler()) { }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ErrorCode = null;
        var response = await base.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var buffer = new MemoryStream();
                var bytes = new byte[1024]; int read;
                while ((read = await stream.ReadAsync(bytes, ct)) > 0)
                {
                    if (buffer.Length + read > 8192) return response;
                    buffer.Write(bytes, 0, read);
                }
                using var doc = JsonDocument.Parse(buffer.ToArray());
                if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object &&
                    error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String)
                    ErrorCode = code.GetString() switch
                    {
                        "insufficient_quota" => "insufficient_quota",
                        "credit_balance_exhausted" => "credit_balance_exhausted",
                        "organization_spend_limit_exceeded" => "organization_spend_limit_exceeded",
                        "project_spend_limit_exceeded" => "project_spend_limit_exceeded",
                        "organization_usage_limit_exceeded" => "organization_usage_limit_exceeded",
                        "rate_limit_exceeded" => "rate_limit_exceeded",
                        "slow_down" => "slow_down",
                        "invalid_api_key" => "invalid_api_key",
                        "model_not_found" => "model_not_found",
                        _ => null
                    };
            }
            catch (JsonException) { }
            catch (IOException) { }
        }
        return response;
    }
}
