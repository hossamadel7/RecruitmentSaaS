using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.TestHost;
using RecruitmentSaaS.Models.Entities;
using RecruitmentSaaS.Services;
using System.Net;
using System.Text.Json;
using Moq;
using Xunit;

namespace RecruitmentSaaS.Tests;
public sealed partial class FoundationTests
{
    private static AiSalesOperationsService Operations(RecruitmentSaaS.Data.RecruitmentCrmContext db, Seed s) => new(db, Control(db, s),
        Options.Create(new AiSalesEngineOptions()), Options.Create(new AiVoiceOptions()), Options.Create(new AiOfficeBookingOptions()));
    [Fact]
    public async Task Operations_are_tenant_scoped_preserve_unknown_usage_and_do_not_claim_release_readiness()
    {
        var s = await SeedAsync(); var seed = await OfficeSeedAsync(s); var other = await SeedAsync(); await EngineSeedAsync(other);
        await using var db = fixture.Create();
        var booking = await Office(db, s).SaveAsync(s.Chat, null, s.Admin, Request(seed), "operations-office", default);
        db.AiSalesDrafts.AddRange(new AiSalesDraft { TenantId = s.Tenant, ConversationId = s.Chat, AgentId = s.Agent, RequestedById = s.Admin,
            RequestKey = "operations-draft", Status = AiDraftStatus.Failed, ErrorCode = "synthetic-private-error", Model = "synthetic", CreatedAt = DateTime.UtcNow.AddSeconds(-1), CompletedAt = DateTime.UtcNow },
            new AiSalesDraft { TenantId = other.Tenant, ConversationId = other.Chat, AgentId = other.Agent, RequestedById = other.Admin,
                RequestKey = "foreign-operations-draft", Status = AiDraftStatus.Reviewed, InputTokens = 90000, OutputTokens = 90000, Model = "foreign-synthetic", CreatedAt = DateTime.UtcNow.AddSeconds(-1), CompletedAt = DateTime.UtcNow });
        await db.SaveChangesAsync(); var ids = await AudioSeedAsync(s, 1);
        var turn = new AiVoiceTurn { TenantId = s.Tenant, ConversationId = s.Chat, AgentId = s.Agent, OwnershipVersion = 1,
            Status = AiVoiceStatus.Reviewed, Transcript = "Private synthetic transcript", ReviewedText = "Private synthetic review" };
        db.AiVoiceTurns.Add(turn); db.AiVoiceParts.Add(new() { MessageId = ids[0], TurnId = turn.Id, Status = AiVoicePartStatus.Completed,
            LastAttemptAt = DateTime.UtcNow, AttemptsOnLastDay = 2, InputTokens = 30, OutputTokens = 10 }); await db.SaveChangesAsync();
        var result = await Operations(db, s).ReadAsync(default);
        Assert.Equal("Failed", Assert.Single(result.Drafts).Status); Assert.Equal(1, result.UnreportedDraftUsage); Assert.Equal(0, result.ReportedDraftInputTokens);
        Assert.Equal(2, result.VoiceAttemptsToday); Assert.Equal(1, result.VoiceAttemptsTodayWithoutReportedUsage); Assert.Equal(30, result.ReportedVoiceInputTokens);
        Assert.Equal(1, result.ConfirmedBookingsWithoutAttendanceEvidence); Assert.Equal(0, result.VerifiedVisits);
        Assert.Null(result.CostEstimate); Assert.False(result.LivePilotQualified); Assert.False(result.AutomaticDispatchAvailable); Assert.NotNull(result.DraftLatency.P95Milliseconds);
        var json = JsonSerializer.Serialize(result); Assert.DoesNotContain("Private synthetic", json); Assert.DoesNotContain("synthetic-private-error", json); Assert.DoesNotContain("90000", json);
        Assert.Contains(result.Gates, g => g.Code == "RealModelEvaluation" && g.State == "review_required"); Assert.NotEmpty(result.PendingMigrations);
    }
    [Fact]
    public async Task Operations_flag_old_jobs_without_retrying_or_mutating_them()
    {
        var s = await SeedAsync(); await EngineSeedAsync(s); await using var db = fixture.Create();
        var draft = new AiSalesDraft { TenantId = s.Tenant, ConversationId = s.Chat, AgentId = s.Agent, RequestedById = s.Admin,
            RequestKey = "operations-interrupted", Status = AiDraftStatus.Generating, CreatedAt = DateTime.UtcNow.AddMinutes(-3) };
        var voice = new AiVoiceTurn { TenantId = s.Tenant, ConversationId = s.Chat, AgentId = s.Agent, Status = AiVoiceStatus.Pending, AvailableAt = DateTime.UtcNow.AddMinutes(-11) };
        db.AddRange(draft, voice); await db.SaveChangesAsync();
        var snapshot = await Operations(db, s).ReadAsync(default);
        Assert.Equal(1, snapshot.InterruptedDrafts); Assert.Equal(1, snapshot.OverdueVoiceTurns); Assert.Equal("attention", snapshot.Gates.Single(g => g.Code == "QueueRecovery").State);
        Assert.Null(snapshot.DraftLatency.P50Milliseconds); Assert.Equal(AiDraftStatus.Generating, draft.Status); Assert.Equal(AiVoiceStatus.Pending, voice.Status);
    }
    [Fact]
    public async Task Operations_latency_sampling_is_bounded_and_empty_samples_stay_unknown()
    {
        var s = await SeedAsync(); await EngineSeedAsync(s); await using var db = fixture.Create();
        Assert.Null((await Operations(db, s).ReadAsync(default)).DraftLatency.P95Milliseconds);
        db.AiSalesDrafts.AddRange(Enumerable.Range(0, 1002).Select(i => new AiSalesDraft { TenantId = s.Tenant, ConversationId = s.Chat,
            AgentId = s.Agent, RequestedById = s.Admin, RequestKey = "latency-" + i, Status = AiDraftStatus.Rejected, Model = "local-policy",
            CreatedAt = DateTime.UtcNow.AddSeconds(-2), CompletedAt = DateTime.UtcNow })); await db.SaveChangesAsync();
        var result = await Operations(db, s).ReadAsync(default);
        Assert.Equal(1000, result.DraftLatency.Samples); Assert.True(result.DraftLatency.Truncated); Assert.True(result.DraftLatency.P50Milliseconds >= 0);
        Assert.Equal(1002, result.LocalPolicyDrafts); Assert.Equal(0, result.UnreportedDraftUsage);
    }
    [Fact]
    public async Task Operational_endpoint_is_admin_only_and_exposes_no_keys_or_customer_text()
    {
        var s = await SeedAsync(); await using var app = await StartHttpAsync(s); using var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ai-sales-operations")).StatusCode);
        await Authenticate(client, s.Human, "3"); Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ai-sales-operations")).StatusCode);
        await Authenticate(client, s.Admin, "7"); Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ai-sales-operations")).StatusCode);
        await Authenticate(client, s.Admin, "1"); var response = await client.GetAsync("/api/ai-sales-operations"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain("apiKey", json); Assert.DoesNotContain("Password", json); Assert.DoesNotContain("Synthetic customer", json);
    }
    [Fact]
    public async Task Two_pending_provider_calls_block_a_third_generation_for_the_same_tenant()
    {
        var first = await SeedAsync(); await EngineSeedAsync(first);
        async Task<Seed> Another()
        {
            var seed = (await SeedAsync()) with { Tenant = first.Tenant };
            await using (var setup = fixture.Create()) { (await setup.AiSalesAgents.FindAsync(seed.Agent))!.TenantId = first.Tenant; await setup.SaveChangesAsync(); }
            await EngineSeedAsync(seed); return seed;
        }
        var second = await Another(); var third = await Another();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var calls = 0;
        var provider = new Mock<IAiSalesDraftProvider>(MockBehavior.Strict);
        provider.Setup(p => p.GenerateAsync(It.IsAny<AiSalesContext>(), It.IsAny<CancellationToken>())).Returns(async (AiSalesContext context, CancellationToken ct) =>
        {
            if (Interlocked.Increment(ref calls) == 1) firstStarted.TrySetResult(); else secondStarted.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(15));
            return new AiDraftProposal("تجربة مراجعة", "qualification", "Synthetic concurrency check", true, [], "synthetic", 10, 5);
        });
        await using var firstDb = fixture.Create(); await using var secondDb = fixture.Create(); await using var thirdDb = fixture.Create();
        Task<AiSalesDraft>? one = null, two = null;
        try
        {
            one = Engine(firstDb, first, provider.Object).GenerateAsync(first.Chat, first.Admin, 1, "concurrent-one", default);
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            two = Engine(secondDb, second, provider.Object).GenerateAsync(second.Chat, second.Admin, 1, "concurrent-two", default);
            await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.ThrowsAsync<AiSalesConflict>(() => Engine(thirdDb, third, provider.Object).GenerateAsync(third.Chat, third.Admin, 1, "concurrent-three", default));
            Assert.Equal(2, calls);
        }
        finally
        {
            release.TrySetResult();
            if (one != null) await one.WaitAsync(TimeSpan.FromSeconds(10));
            if (two != null) await two.WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.Equal(2, await thirdDb.AiSalesDrafts.CountAsync(d => d.TenantId == first.Tenant));
        Assert.False(await thirdDb.WhatsAppMessages.AnyAsync(m => m.ConversationId == third.Chat && m.Direction == (byte)MessageDirection.Outgoing));
    }
}
