using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace RecruitmentSaaS.Services;
public sealed record AiKnowledgeMatch(AiSalesKnowledge Knowledge, double? SemanticScore, string Reason);
public sealed class AiSemanticKnowledgeService(RecruitmentCrmContext db, AiSalesControlService controls,
    AiSalesLearningService learning, IAiLearningProvider provider, IOptions<AiSalesLearningOptions> options, IMemoryCache? cache = null)
{
    public int CandidateLimit => options.Value.Enabled ? options.Value.MaxIndexedKnowledge : 500;
    public static double Cosine(float[] a, float[] b)
    {
        if (a.Length == 0 || a.Length != b.Length || a.Any(v => !float.IsFinite(v)) || b.Any(v => !float.IsFinite(v))) return 0;
        double dot = 0, aa = 0, bb = 0;
        for (var n = 0; n < a.Length; n++) { dot += (double)a[n] * b[n]; aa += (double)a[n] * a[n]; bb += (double)b[n] * b[n]; }
        return aa == 0 || bb == 0 ? 0 : dot / Math.Sqrt(aa * bb);
    }
    public async Task IndexAsync(Guid account, CancellationToken ct)
    {
        learning.Require(account, true);
        var now = DateTime.UtcNow;
        var knowledge = await db.AiSalesKnowledge.AsNoTracking().Where(k => k.TenantId == learning.Tenant && k.WhatsAppAccountId == account &&
            k.Status == AiKnowledgeStatus.Approved && k.ValidFrom <= now && k.ValidUntil > now).OrderBy(k => k.Id).Take(options.Value.MaxIndexedKnowledge).ToListAsync(ct);
        foreach (var k in knowledge)
        {
            await using var gate = await ConversationLock.AcquireAsync(db, "learning-index:" + k.Id, ct);
            var index = await db.AiKnowledgeIndexes.SingleOrDefaultAsync(i => i.KnowledgeId == k.Id && i.TenantId == learning.Tenant, ct);
            var hash = AiLearningSourceService.Hash(IntakeScript.Normalize(k.Content));
            if (index?.KnowledgeVersion == k.Version && index.EmbeddingModel == options.Value.EmbeddingModel && index.Embedding.Length == options.Value.EmbeddingDimensions && index.ContentHash == hash) continue;
            if (index != null && index.SourceFingerprint.Length > 0 && !await learning.SourceCurrentAsync(index.AnalysisId, ct)) continue;
            var usage = await learning.ReserveAsync("embedding", options.Value.EmbeddingModel, ct);
            AiEmbeddingResponse result;
            try { result = await provider.EmbedAsync(AiLearningSourceService.Redact(k.Title + "\n" + k.Content, []), ct); }
            catch { usage.Status = "FailedOrInterrupted"; await db.SaveChangesAsync(CancellationToken.None); throw; }
            usage.InputTokens = result.InputTokens; usage.Status = "Completed";
            usage.Model = result.Model ?? options.Value.EmbeddingModel;
            var current = await db.AiSalesKnowledge.AsNoTracking().SingleOrDefaultAsync(x => x.Id == k.Id && x.TenantId == learning.Tenant, ct);
            if (current?.Status != AiKnowledgeStatus.Approved || current.Version != k.Version || current.ValidUntil <= DateTime.UtcNow) { await db.SaveChangesAsync(ct); continue; }
            if (index == null) { index = new() { KnowledgeId = k.Id, TenantId = learning.Tenant, WhatsAppAccountId = account }; db.Add(index); }
            index.ContentHash = hash; index.Embedding = result.Vector; index.EmbeddingModel = options.Value.EmbeddingModel;
            index.KnowledgeVersion = k.Version; index.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }
    public async Task<AiKnowledgeMatch[]> RankAsync(Guid tenant, Guid account, string query, AiSalesKnowledge[] candidates, CancellationToken ct)
    {
        if (tenant != controls.Options.TenantId) throw new AiSalesValidation("Invalid retrieval tenant.");
        var now = DateTime.UtcNow;
        candidates = candidates.Where(k => k.TenantId == tenant && k.WhatsAppAccountId == account && k.Status == AiKnowledgeStatus.Approved && k.ValidFrom <= now && k.ValidUntil > now).ToArray();
        if (!options.Value.Enabled && candidates.All(k => !k.SourceReference.StartsWith("Analysis ", StringComparison.Ordinal)))
        {
            var words = IntakeScript.Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct().Take(30).ToArray();
            return candidates.OrderByDescending(k => words.Count(t => IntakeScript.Normalize(k.Title + " " + k.Content).Contains(t)))
                .ThenByDescending(k => k.ApprovedAt).Select(k => new AiKnowledgeMatch(k, null, "Learning disabled; existing lexical retrieval")).ToArray();
        }
        var ids = candidates.Select(k => k.Id).ToArray();
        var indexes = await db.AiKnowledgeIndexes.AsNoTracking().Where(i => i.TenantId == tenant && i.WhatsAppAccountId == account && ids.Contains(i.KnowledgeId)).ToListAsync(ct);
        var current = new List<AiSalesKnowledge>();
        foreach (var k in candidates)
        {
            var index = indexes.SingleOrDefault(i => i.KnowledgeId == k.Id);
            if (index == null && k.SourceReference.StartsWith("Analysis ", StringComparison.Ordinal)) continue;
            current.Add(k);
        }
        float[]? vector = null;
        if (options.Value.Enabled && options.Value.SemanticRetrievalEnabled && options.Value.ExternalProcessingApproved && options.Value.PrivacyApproved &&
            options.Value.ApprovedAccountIds.Contains(account) && !string.IsNullOrWhiteSpace(query) && indexes.Any(i => i.Embedding.Length == options.Value.EmbeddingDimensions &&
                i.EmbeddingModel == options.Value.EmbeddingModel && current.Any(k => k.Id == i.KnowledgeId && k.Version == i.KnowledgeVersion)))
        {
            try {
                var cacheKey = "ai-query:" + tenant + ":" + account + ":" + options.Value.EmbeddingModel + ":" + options.Value.EmbeddingDimensions + ":" + AiLearningSourceService.Hash(query);
                if (cache?.TryGetValue<float[]>(cacheKey, out var cached) == true) vector = cached;
                else {
                var usage = await learning.ReserveAsync("query_embedding", options.Value.EmbeddingModel, ct);
                try { var result = await provider.EmbedAsync(AiLearningSourceService.Redact(query, []), ct); vector = result.Vector; usage.InputTokens = result.InputTokens; usage.Status = "Completed";
                    usage.Model = result.Model ?? options.Value.EmbeddingModel;
                    cache?.Set(cacheKey, vector, TimeSpan.FromMinutes(5)); }
                catch { usage.Status = "FailedOrInterrupted"; throw; }
                finally { await db.SaveChangesAsync(CancellationToken.None); }
                }
            } catch (Exception ex) when (ex is not OperationCanceledException) { /* Explicit lexical fallback; no claim of semantic coverage. */ }
        }
        var terms = IntakeScript.Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct().Take(30).ToArray();
        var ranked = current.Select(k => {
            var index = indexes.SingleOrDefault(i => i.KnowledgeId == k.Id);
            var eligible = vector != null && index?.KnowledgeVersion == k.Version && index.EmbeddingModel == options.Value.EmbeddingModel && index.Embedding.Length == vector.Length &&
                index.ContentHash == AiLearningSourceService.Hash(IntakeScript.Normalize(k.Content));
            double? score = eligible ? Cosine(vector!, index!.Embedding) : null;
            var lexical = terms.Count(t => IntakeScript.Normalize(k.Title + " " + k.Content).Contains(t));
            return (Match: new AiKnowledgeMatch(k, score, score == null ? "Lexical fallback; no current compatible embedding" : "Cosine over current approved knowledge"), Rank: (score ?? 0) + Math.Min(.2, lexical * .01));
        }).OrderByDescending(x => x.Rank).ThenByDescending(x => x.Match.Knowledge.ApprovedAt).Select(x => x.Match).ToArray();
        var sourceValidity = new Dictionary<Guid, bool>();
        var acceptedMatches = new List<AiKnowledgeMatch>();
        foreach (var match in ranked)
        {
            if (acceptedMatches.Count(m => m.Knowledge.Kind == match.Knowledge.Kind) >= (match.Knowledge.Kind == AiKnowledgeKind.Fact ? 12 : 4)) continue;
            var index = indexes.SingleOrDefault(i => i.KnowledgeId == match.Knowledge.Id);
            if (index?.SourceFingerprint.Length > 0)
            {
                if (index.AnalysisId == null) continue;
                if (!sourceValidity.TryGetValue(index.AnalysisId.Value, out var valid))
                {
                    if (sourceValidity.Count >= 25) continue; // Suppress unchecked sources rather than exceed the per-request read budget.
                    valid = await learning.SourceCurrentAsync(index.AnalysisId, ct); sourceValidity[index.AnalysisId.Value] = valid;
                }
                if (!valid) continue;
            }
            acceptedMatches.Add(match);
        }
        return acceptedMatches.ToArray();
    }
}
