using System.Text.Json;
using Xunit;

namespace RecruitmentSaaS.Tests;
public sealed class ReplayHarnessTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    private static string PackPath => Path.Combine(Directory.GetCurrentDirectory(), "docs", "evaluation", "synthetic-sales-cases.json");
    [Theory]
    [InlineData("EG-02")]
    [InlineData("EG-15")]
    [InlineData("EG-16")]
    public async Task Structural_replay_preserves_unknown_fields_valid_retired_intervals_and_takeover_without_claiming_quality(string id)
    {
        var pack = SyntheticReplayHarness.Load(PackPath);
        var result = await SyntheticReplayHarness.RunAsync(fixture, pack, pack.Cases.Single(c => c.Id == id), 1);
        Assert.True(result.StructuralPassed, result.FailureCode); Assert.Equal("not_run", result.SemanticAssessment);
        if (id == "EG-15") Assert.Equal("local-policy", result.Path);
        if (id == "EG-16") { Assert.Equal("Stale", result.DraftStatus); Assert.Equal("", result.Reply); }
    }
    [Fact]
    public void Runner_rejects_unmarked_data_and_playbook_fact_citations_before_processing()
    {
        var path = Path.GetTempFileName();
        try
        {
            var pack = SyntheticReplayHarness.Load(PackPath); pack.SyntheticOnly = false;
            File.WriteAllText(path, JsonSerializer.Serialize(pack)); Assert.Throws<InvalidOperationException>(() => SyntheticReplayHarness.Load(path));
            pack.SyntheticOnly = true; var scenario = pack.Cases.Single(c => c.Id == "EG-10"); scenario.Expected.FactCitationsAllowed = ["style"];
            File.WriteAllText(path, JsonSerializer.Serialize(pack)); Assert.Throws<InvalidOperationException>(() => SyntheticReplayHarness.Load(path));
        }
        finally { File.Delete(path); }
    }
}
