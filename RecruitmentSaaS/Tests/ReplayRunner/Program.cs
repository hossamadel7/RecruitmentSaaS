using RecruitmentSaaS.Tests;
using System.Text.Json;
using System.Security.Cryptography;
using RecruitmentSaaS.Services;
using System.Diagnostics;

// Reads no production configuration, registers no transport/provider/worker, accepts only the fixture's dedicated test instance.
if (args.Length is < 1 or > 2) throw new ArgumentException("Usage: ReplayRunner <absolute workspace path> [absolute output directory]");
if (!Path.IsPathFullyQualified(args[0]) || args.Length == 2 && !Path.IsPathFullyQualified(args[1])) throw new ArgumentException("Absolute paths are required.");
var root = Path.GetFullPath(args[0]); var output = args.Length == 2 ? Path.GetFullPath(args[1]) : Path.Combine(root, "docs", "validation");
var pack = SyntheticReplayHarness.Load(Path.Combine(root, "docs", "evaluation", "synthetic-sales-cases.json"));
var fixture = new DatabaseFixture(); await fixture.InitializeAsync();
try
{
    var results = new List<SyntheticReplayResult>();
    for (var repeat = 1; repeat <= 3; repeat++)
        foreach (var scenario in pack.Cases)
        {
            var row = await SyntheticReplayHarness.RunAsync(fixture, pack, scenario, repeat); results.Add(row);
            Console.WriteLine($"{row.CaseId} repeat {repeat}: {(row.StructuralPassed ? "structural checks passed" : "FAILED " + row.FailureCode)}; semantic assessment not run");
        }
    var burstTimer = Stopwatch.StartNew();
    var burst = await Task.WhenAll(pack.Cases.Take(8).Select(scenario => SyntheticReplayHarness.RunAsync(fixture, pack, scenario, 4)));
    burstTimer.Stop();
    var elapsed = results.Select(r => r.ElapsedMilliseconds).Order().ToArray();
    double? Percentile(double p) => elapsed.Length == 0 ? null : elapsed[(int)Math.Ceiling(elapsed.Length * p) - 1];
    Directory.CreateDirectory(output);
    var report = new { generatedAt = DateTime.UtcNow, fixtureVersion = pack.Version, promptVersionReference = OpenAiSalesDraftProvider.PromptVersion,
        caseFileSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(root, "docs", "evaluation", "synthetic-sales-cases.json")))),
        syntheticOnly = true, externalCalls = 0, realModelEvaluationPerformed = false,
        semanticAssessment = "not_run", livePilotQualified = false, runs = results.Count, structuralPassed = results.Count(r => r.StructuralPassed),
        localPolicyRuns = results.Count(r => r.Path == "local-policy"), deterministicFakeProviderRuns = results.Count(r => r.Path == "deterministic-fake-provider"),
        isolatedLocalLatency = new { p50Milliseconds = Percentile(.5), p95Milliseconds = Percentile(.95), includesSeedingAndDatabaseChecks = true, productionSloEvidence = false },
        concurrentBurst = new { concurrency = burst.Length, separateSyntheticTenants = true, passed = burst.Count(r => r.StructuralPassed),
            elapsedMilliseconds = burstTimer.Elapsed.TotalMilliseconds, productionSloEvidence = false, results = burst }, results };
    var path = Path.Combine(output, "phase5-synthetic-replay.json");
    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("Synthetic structural report: " + path);
    Console.WriteLine($"Concurrent synthetic burst: {burst.Count(r => r.StructuralPassed)}/{burst.Length} structural checks passed; not production load evidence");
    Environment.ExitCode = results.Concat(burst).All(r => r.StructuralPassed) ? 0 : 1;
}
finally { await fixture.DisposeAsync(); }
