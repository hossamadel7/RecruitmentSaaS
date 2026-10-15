using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Services;

public sealed record AiOperationalGate(string Code, string State, string Detail);
public sealed record AiStatusCount(string Status, int Count);
public sealed record AiLatencySummary(int Samples, bool Truncated, double? P50Milliseconds, double? P95Milliseconds);
public sealed record AiOperationsSnapshot(DateTime GeneratedAt, DateTime WindowStart, string Scope,
    AiStatusCount[] Drafts, AiStatusCount[] VoiceTurns, AiStatusCount[] Bookings,
    int LocalPolicyDrafts, int UnreportedDraftUsage, long ReportedDraftInputTokens, long ReportedDraftOutputTokens,
    int VoiceAttemptsToday, int UnreportedVoiceUsage, int VoiceRecordsWithReportedTokens, int VoiceRecordsWithReportedSeconds,
    int VoiceAttemptsTodayWithoutReportedUsage, long ReportedVoiceInputTokens, long ReportedVoiceOutputTokens,
    double ReportedVoiceSeconds, int ReviewedVoiceWithoutProbability, int OverdueVoiceTurns, int InterruptedDrafts,
    int VerifiedVisits, int ConfirmedBookingsWithoutAttendanceEvidence, AiLatencySummary DraftLatency,
    string[] PendingMigrations, AiOperationalGate[] Gates, bool LivePilotQualified, bool AutomaticDispatchAvailable, decimal? CostEstimate);

/// <summary>Read-only aggregates. No customer text, provider bodies, secrets or unscoped webhook payloads.</summary>
public sealed class AiSalesOperationsService(RecruitmentCrmContext db, AiSalesControlService controls,
    IOptions<AiSalesEngineOptions> engine, IOptions<AiVoiceOptions> voice, IOptions<AiOfficeBookingOptions> office)
{
    public async Task<AiOperationsSnapshot> ReadAsync(CancellationToken ct)
    {
        var tenant = controls.Options.TenantId; var now = DateTime.UtcNow; var since = now.AddHours(-24); var day = now.Date;
        var drafts = db.AiSalesDrafts.AsNoTracking().Where(d => d.TenantId == tenant && d.CreatedAt >= since);
        var turns = db.AiVoiceTurns.AsNoTracking().Where(t => t.TenantId == tenant && t.CreatedAt >= since);
        var bookings = db.AiOfficeBookings.AsNoTracking().Where(b => b.TenantId == tenant && b.CreatedAt >= since);
        var draftCounts = await drafts.GroupBy(d => d.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        var voiceCounts = await turns.GroupBy(t => t.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        var bookingCounts = await bookings.GroupBy(b => b.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
        var providerDrafts = drafts.Where(d => d.Model != "local-policy");
        var draftUsage = await providerDrafts.GroupBy(d => 1).Select(g => new { Unknown = g.Count(d => d.InputTokens == null || d.OutputTokens == null),
            Input = g.Sum(d => (long?)d.InputTokens) ?? 0, Output = g.Sum(d => (long?)d.OutputTokens) ?? 0 }).SingleOrDefaultAsync(ct);
        var parts = db.AiVoiceParts.AsNoTracking().Where(p => p.LastAttemptAt >= since && db.AiVoiceTurns.Any(t => t.Id == p.TurnId && t.TenantId == tenant));
        var voiceUsage = await parts.GroupBy(p => 1).Select(g => new { Unknown = g.Count(p => (p.InputTokens == null || p.OutputTokens == null) && p.ReportedSeconds == null),
            TokensKnown = g.Count(p => p.InputTokens != null && p.OutputTokens != null), SecondsKnown = g.Count(p => p.ReportedSeconds != null),
            Input = g.Sum(p => (long?)p.InputTokens) ?? 0, Output = g.Sum(p => (long?)p.OutputTokens) ?? 0, Seconds = g.Sum(p => p.ReportedSeconds) ?? 0 }).SingleOrDefaultAsync(ct);
        var attempts = await parts.Where(p => p.LastAttemptAt >= day).SumAsync(p => (int?)p.AttemptsOnLastDay, ct) ?? 0;
        var reportedAttemptsToday = await parts.CountAsync(p => p.LastAttemptAt >= day && (p.InputTokens != null && p.OutputTokens != null || p.ReportedSeconds != null), ct);
        var completed = await drafts.Where(d => d.CompletedAt != null).OrderByDescending(d => d.CompletedAt)
            .Select(d => new { d.CreatedAt, d.CompletedAt }).Take(1001).ToListAsync(ct);
        var duration = completed.Take(1000).Select(d => Math.Max(0, (d.CompletedAt!.Value - d.CreatedAt).TotalMilliseconds)).Order().ToArray();
        var migrations = (await db.Database.GetPendingMigrationsAsync(ct)).ToArray();
        var overdue = await db.AiVoiceTurns.CountAsync(t => t.TenantId == tenant &&
            (t.Status == AiVoiceStatus.Pending && t.AvailableAt < now.AddMinutes(-10) || t.Status == AiVoiceStatus.Processing && t.StartedAt < now.AddMinutes(-10)), ct);
        var interrupted = await db.AiSalesDrafts.CountAsync(d => d.TenantId == tenant && d.Status == AiDraftStatus.Generating && d.CreatedAt < now.AddMinutes(-2), ct);
        var facts = await db.AiSalesKnowledge.CountAsync(k => k.TenantId == tenant && k.Kind == AiKnowledgeKind.Fact && k.Status == AiKnowledgeStatus.Approved && k.ValidFrom <= now && k.ValidUntil > now, ct);
        var policyCount = await db.AiOfficePolicies.CountAsync(p => p.TenantId == tenant && p.Enabled && p.ValidUntil > now, ct);
        var gates = new List<AiOperationalGate>
        {
            new("Foundation", controls.Options.Enabled && tenant != Guid.Empty ? "pass" : "disabled", "Foundation must be enabled with a deployment tenant."),
            new("DatabaseMigrations", migrations.Length == 0 ? "pass" : "review_required", "Migration history is evidence of application only; staging rehearsal and backup verification remain required."),
            new("DraftProvider", engine.Value.DraftsEnabled && engine.Value.ExternalProcessingApproved && !string.IsNullOrWhiteSpace(engine.Value.ApiKey) && !string.IsNullOrWhiteSpace(engine.Value.Model) ? "configured" : "disabled", "Configuration does not establish connectivity or model quality."),
            new("VoiceProvider", voice.Value.Enabled && voice.Value.ExternalProcessingApproved && !string.IsNullOrWhiteSpace(voice.Value.ApiKey) && OpenAiVoiceTranscriber.SupportedModel(voice.Value.Model) ? "configured" : "disabled", "Approved real audio evaluation is still required."),
            new("KnowledgeReview", "review_required", $"{facts} currently approved fact entries; counts do not establish real business accuracy or account coverage."),
            new("OfficePolicies", office.Value.Enabled && policyCount > 0 ? "configured" : "disabled", $"{policyCount} currently enabled policies; verify actual schedules and branch coverage."),
            new("QueueRecovery", overdue == 0 && interrupted == 0 ? "pass" : "attention", $"{overdue} overdue voice turns; {interrupted} interrupted draft reservations. Inspect scoped records before manual retry."),
            new("RealModelEvaluation", "review_required", "Approved representative replay, human semantic scores, repeated model runs and critical-violation review are not established by this dashboard."),
            new("PrivacyAndRetention", "review_required", "Approve external processing, access, transcript retention, deletion and historical-data handling before live use."),
            new("OperationalAcceptance", "review_required", "Agree latency/spend limits, rehearse failures and restore, evaluate staging load and monitor a supervised pilot."),
            new("AutomaticRelease", "unavailable", "Administrator dispatch of reviewed text is implemented behind a separate release gate. Autonomous dispatch and weighted AI rotation remain unavailable.")
        };
        return new(now, since, "Configured deployment tenant; current state of records created in the last 24 hours unless explicitly labelled otherwise.",
            draftCounts.Select(x => new AiStatusCount(x.Status.ToString(), x.Count)).ToArray(), voiceCounts.Select(x => new AiStatusCount(x.Status.ToString(), x.Count)).ToArray(),
            bookingCounts.Select(x => new AiStatusCount(x.Status.ToString(), x.Count)).ToArray(), await drafts.CountAsync(d => d.Model == "local-policy", ct),
            draftUsage?.Unknown ?? 0, draftUsage?.Input ?? 0, draftUsage?.Output ?? 0, attempts, voiceUsage?.Unknown ?? 0, voiceUsage?.TokensKnown ?? 0, voiceUsage?.SecondsKnown ?? 0,
            Math.Max(0, attempts - reportedAttemptsToday), voiceUsage?.Input ?? 0, voiceUsage?.Output ?? 0,
            voiceUsage?.Seconds ?? 0, await turns.CountAsync(t => t.Status == AiVoiceStatus.Reviewed && t.LowestTokenProbability == null, ct), overdue, interrupted,
            await bookings.CountAsync(b => b.VerifiedVisitId != null && db.LeadVisits.Any(v => v.Id == b.VerifiedVisitId), ct),
            await bookings.CountAsync(b => b.Status == AiOfficeBookingStatus.Confirmed && b.VerifiedVisitId == null, ct),
            new(duration.Length, completed.Count > 1000, Percentile(duration, .5), Percentile(duration, .95)), migrations, gates.ToArray(), false, false, null);
    }
    private static double? Percentile(double[] sorted, double quantile) => sorted.Length == 0 ? null : sorted[Math.Max(0, (int)Math.Ceiling(sorted.Length * quantile) - 1)];
}
