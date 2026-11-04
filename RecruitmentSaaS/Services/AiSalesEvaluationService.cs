using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace RecruitmentSaaS.Services;
public sealed record AiEvaluationCaseRequest(Guid AccountId, [Required, MaxLength(100)] string Dataset,
    [Required, MaxLength(100)] string Scenario, [Required, MaxLength(2000)] string Expected,
    AiSalesContext Context, bool PrivacyReviewed, Guid? AnalysisId = null);
public sealed record AiEvaluationRunRequest(Guid AccountId, [Required, MaxLength(100)] string Dataset,
    [Required, MaxLength(100)] string Configuration, [Required, MaxLength(100)] string Baseline);
public sealed record AiEvaluationScoreRequest(bool Passed, [Required, MaxLength(2000)] string Review);

public sealed class AiSalesEvaluationService(RecruitmentCrmContext db, AiSalesLearningService learning, IAiSalesDraftProvider provider)
{
    public Task<AiEvaluationCase[]> ListAsync(Guid account, CancellationToken ct) => db.AiEvaluationCases.AsNoTracking()
        .Where(e => e.TenantId == learning.Tenant && e.WhatsAppAccountId == account).OrderBy(e => e.Dataset).ThenBy(e => e.Id).Take(100).ToArrayAsync(ct);
    public async Task<AiEvaluationCase> AddAsync(AiEvaluationCaseRequest r, Guid actor, CancellationToken ct)
    {
        learning.Require(r.AccountId);
        await using var datasetGate = await ConversationLock.AcquireAsync(db, "evaluation-dataset:" + learning.Tenant + ":" + r.AccountId + ":" + r.Dataset.Trim(), ct);
        if (!r.PrivacyReviewed || r.Context == null || r.Context.RecentMessages.Length > 40 || r.Context.Knowledge.Length > 8 ||
            r.Context.RecentMessages.Any(m => m.Text.Length > 4000) || JsonSerializer.Serialize(r.Context).Length > 40000)
            throw new AiSalesValidation("Bound and privacy-review the evaluation context.");
        if (r.AnalysisId != null && !await db.AiConversationAnalyses.AnyAsync(a => a.Id == r.AnalysisId && a.TenantId == learning.Tenant && a.WhatsAppAccountId == r.AccountId && a.State == AiLearningState.Completed, ct))
            throw new AiSalesValidation("Scoped analysis unavailable.");
        // Freeze datasets once results exist so baseline/candidate runs cannot compare different case definitions.
        if (await db.AiEvaluationResults.AnyAsync(x => x.TenantId == learning.Tenant && db.AiEvaluationCases.Any(c => c.Id == x.CaseId && c.WhatsAppAccountId == r.AccountId && c.Dataset == r.Dataset), ct))
            throw new AiSalesConflict("Dataset is frozen after its first run; use a new dataset version.");
        var item = new AiEvaluationCase { TenantId = learning.Tenant, WhatsAppAccountId = r.AccountId, AnalysisId = r.AnalysisId,
            Dataset = r.Dataset.Trim(), Scenario = r.Scenario.Trim(), Expected = AiLearningSourceService.Redact(r.Expected, []),
            ContextJson = JsonSerializer.Serialize(r.Context with {
                Customer = r.Context.Customer with { Name = "", ReviewedSummary = AiLearningSourceService.Redact(r.Context.Customer.ReviewedSummary, []) },
                RecentMessages = r.Context.RecentMessages.Select(m => m with { Text = AiLearningSourceService.Redact(m.Text, []) }).ToArray()
            }), PrivacyApproved = true };
        db.Add(item); learning.Audit("AiEvaluationCaseCreated", item.Id, actor); await db.SaveChangesAsync(ct); return item;
    }
    public async Task<object> RunAsync(AiEvaluationRunRequest r, Guid actor, CancellationToken ct)
    {
        learning.Require(r.AccountId, true);
        await using var datasetGate = await ConversationLock.AcquireAsync(db, "evaluation-dataset:" + learning.Tenant + ":" + r.AccountId + ":" + r.Dataset.Trim(), ct);
        var cases = await db.AiEvaluationCases.AsNoTracking().Where(e => e.TenantId == learning.Tenant && e.WhatsAppAccountId == r.AccountId && e.Dataset == r.Dataset && e.PrivacyApproved).OrderBy(e => e.Id).Take(51).ToArrayAsync(ct);
        if (cases.Length is 0 or > 50) throw new AiSalesValidation("A run requires 1–50 approved fixed cases; version larger datasets in cohorts.");
        var run = Guid.NewGuid();
        foreach (var item in cases)
        {
            var context = JsonSerializer.Deserialize<AiSalesContext>(item.ContextJson) ?? throw new AiSalesValidation("Invalid case context.");
            var now = DateTime.UtcNow;
            var ids = context.Knowledge.Select(k => k.Id).ToArray();
            var facts = await db.AiSalesKnowledge.AsNoTracking().Where(k => k.TenantId == learning.Tenant && k.WhatsAppAccountId == r.AccountId && ids.Contains(k.Id) &&
                k.Status == AiKnowledgeStatus.Approved && k.ValidFrom <= now && k.ValidUntil > now).ToListAsync(ct);
            if (context.Knowledge.Any(k => !facts.Any(f => f.Id == k.Id && f.Version == k.Version && f.Content == k.Content && f.Kind == k.Kind)))
                throw new AiSalesConflict("Evaluation knowledge changed/expired; create a new dataset version with current approved knowledge.");
            var usage = await learning.ReserveAsync("evaluation", OpenAiSalesDraftProvider.PromptVersion, ct);
            AiDraftProposal proposal;
            try {
                proposal = AiSalesReplyPolicy.Create(context) ?? await provider.GenerateAsync(context, ct);
                if (proposal.SourceIds.Any(id => !facts.Any(f => f.Id == id && f.Kind == AiKnowledgeKind.Fact))) throw new AiSalesValidation("Evaluation reply cited unsupported facts.");
                usage.InputTokens = proposal.InputTokens ?? 0; usage.OutputTokens = proposal.OutputTokens ?? 0;
                usage.Model = proposal.Model[..Math.Min(100, proposal.Model.Length)];
                usage.Status = proposal.InputTokens == null ? "CompletedUsageUnknown" : "Completed";
            } catch { usage.Status = "FailedOrInterrupted"; await db.SaveChangesAsync(CancellationToken.None); throw; }
            var result = new AiEvaluationResult { TenantId = learning.Tenant, CaseId = item.Id, RunId = run, ActorId = actor,
                Configuration = r.Configuration.Trim(), Baseline = r.Baseline.Trim(), Reply = proposal.Reply,
                ReviewJson = JsonSerializer.Serialize(new { prompt = OpenAiSalesDraftProvider.PromptVersion,
                    instructionsFingerprint = AiLearningSourceService.Hash(OpenAiSalesDraftProvider.Instructions), proposal.Model, proposal.Intent,
                    proposal.SourceIds, proposal.NeedsHuman, expected = item.Expected, automaticChecksOnly = true }) };
            db.Add(result); await db.SaveChangesAsync(ct);
        }
        learning.Audit("AiEvaluationRunCompleted", run, actor); await db.SaveChangesAsync(ct);
        return new { runId = run, cases = cases.Length, customerMessagesSent = 0, humanScoringRequired = true };
    }
    public async Task ScoreAsync(Guid id, AiEvaluationScoreRequest r, Guid actor, CancellationToken ct)
    {
        var result = await db.AiEvaluationResults.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == learning.Tenant, ct)
            ?? throw new AiSalesValidation("Evaluation result unavailable.");
        var item = await db.AiEvaluationCases.SingleAsync(c => c.Id == result.CaseId, ct); learning.Require(item.WhatsAppAccountId);
        if (string.IsNullOrWhiteSpace(r.Review)) throw new AiSalesValidation("Record factual support, language, context, objection handling and escalation observations.");
        result.Passed = r.Passed; result.ReviewJson = JsonSerializer.Serialize(new { prior = JsonSerializer.Deserialize<JsonElement>(result.ReviewJson),
            review = AiLearningSourceService.Redact(r.Review, []), reviewer = actor, at = DateTime.UtcNow });
        learning.Audit("AiEvaluationScored", id, actor); await db.SaveChangesAsync(ct);
    }
    public async Task<object> CompareAsync(Guid account, string dataset, string baseline, string candidate, CancellationToken ct)
    {
        learning.Require(account);
        var cases = await db.AiEvaluationCases.AsNoTracking().Where(c => c.TenantId == learning.Tenant && c.WhatsAppAccountId == account && c.Dataset == dataset).Select(c => c.Id).ToArrayAsync(ct);
        var results = await db.AiEvaluationResults.AsNoTracking().Where(r => r.TenantId == learning.Tenant && cases.Contains(r.CaseId) && (r.Configuration == baseline || r.Configuration == candidate)).ToListAsync(ct);
        var latest = results.GroupBy(r => new { r.CaseId, r.Configuration }).Select(g => g.OrderByDescending(r => r.CreatedAt).First()).ToArray();
        var paired = cases.Where(id => latest.Any(r => r.CaseId == id && r.Configuration == baseline && r.Passed != null) && latest.Any(r => r.CaseId == id && r.Configuration == candidate && r.Passed != null)).ToArray();
        return new { dataset, baseline, candidate, totalCases = cases.Length, scoredPairs = paired.Length,
            baselinePassed = latest.Count(r => paired.Contains(r.CaseId) && r.Configuration == baseline && r.Passed == true),
            candidatePassed = latest.Count(r => paired.Contains(r.CaseId) && r.Configuration == candidate && r.Passed == true),
            fullyEvaluated = paired.Length == cases.Length && cases.Length > 0, autoRelease = false,
            results = latest.Select(r => new { r.Id, r.CaseId, r.Configuration, r.Reply, r.Passed, r.ReviewJson }) };
    }
}
