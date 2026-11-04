using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using System.Text.Json;

namespace RecruitmentSaaS.Services;
public sealed class AiSalesLearningService(RecruitmentCrmContext db, AiSalesControlService controls,
    IOptions<AiSalesLearningOptions> settings, AiLearningSourceService sources, IAiLearningProvider provider)
{
    public Guid Tenant => controls.Options.TenantId;
    public static IEnumerable<AiLearningMessage[]> ChunkMessages(IEnumerable<AiLearningMessage> messages) =>
        messages.SelectMany(m => {
            var fragments = new List<string>();
            for (var offset = 0; offset < m.Text.Length;)
            {
                var length = Math.Min(4000, m.Text.Length - offset);
                if (offset + length < m.Text.Length && char.IsHighSurrogate(m.Text[offset + length - 1]) && char.IsLowSurrogate(m.Text[offset + length])) length--;
                fragments.Add(m.Text.Substring(offset, length)); offset += length;
            }
            if (fragments.Count == 0) fragments.Add("");
            return fragments.Select((text, i) => m with { Text = text, Part = i + 1, Parts = fragments.Count });
        }).Chunk(20);
    public void RequireScope(Guid account)
    {
        if (Tenant == Guid.Empty || !settings.Value.ApprovedAccountIds.Contains(account))
            throw new AiSalesValidation("Account outside configured learning business scope.");
    }
    public void Require(Guid account, bool external = false)
    {
        RequireScope(account);
        if (!controls.Options.Enabled || !settings.Value.Enabled)
            throw new AiSalesValidation("Learning disabled or account outside configured business scope.");
        if (external && (!settings.Value.ExternalProcessingApproved || !settings.Value.PrivacyApproved))
            throw new AiSalesValidation("External processing and privacy approval are required.");
    }
    public async Task<AiLearningUsage> ReserveAsync(string kind, string model, CancellationToken ct)
    {
        await using var gate = await ConversationLock.AcquireAsync(db, "learning-budget:" + Tenant, ct);
        var day = DateTime.UtcNow.Date;
        if (await db.AiLearningUsage.CountAsync(u => u.TenantId == Tenant && u.CreatedAt >= day, ct) >= settings.Value.MaxCallsPerDay)
            throw new AiSalesValidation("Daily learning/embedding call limit reached.");
        var usage = new AiLearningUsage { TenantId = Tenant, Kind = kind, Model = model };
        db.Add(usage); await db.SaveChangesAsync(ct); return usage;
    }
    public async Task<int> ScanAsync(Guid account, CancellationToken ct)
    {
        Require(account);
        await using var gate = await ConversationLock.AcquireAsync(db, "learning-scan:" + Tenant + ":" + account, ct);
        var config = await db.AiLearningAccounts.SingleOrDefaultAsync(a => a.TenantId == Tenant && a.WhatsAppAccountId == account, ct);
        if (config == null || config.Paused) return 0;
        var cursor = config.Cursor;
        var chats = await db.WhatsAppConversations.FromSqlInterpolated($"""
            SELECT * FROM demorecruitment."WhatsAppConversations"
            WHERE "WhatsAppAccountId" = {account} AND "Id" > {cursor}
              AND NOT ("Id" = ANY ({config.ExcludedConversationIds}))
              AND ({config.IncludeOpenHistory} OR "Status" = {(byte)ConversationStatus.Closed})
            ORDER BY "Id" LIMIT {settings.Value.ScanBatchSize}
            """).AsNoTracking().ToListAsync(ct);
        var added = 0;
        foreach (var chat in chats)
        {
            AiLearningSource source;
            try { source = await sources.ReadAsync(account, chat.Id, ct); }
            catch (AiSalesValidation)
            {
                var unreadFingerprint = AiLearningSourceService.Hash($"unread:{chat.Id}:{chat.LastMessageAt:O}:{settings.Value.MaxMessagesPerConversation}");
                if (await db.WhatsAppConversations.AnyAsync(c => c.Id == chat.Id && c.WhatsAppAccountId == account, ct) &&
                    !await db.AiConversationAnalyses.AnyAsync(a => a.TenantId == Tenant && a.ConversationId == chat.Id && a.Fingerprint == unreadFingerprint, ct))
                { db.Add(new AiConversationAnalysis { TenantId = Tenant, WhatsAppAccountId = account, ConversationId = chat.Id, Fingerprint = unreadFingerprint,
                    State = AiLearningState.Failed, Attempts = 5, ErrorCode = "SourceRequiresLargerBoundOrReview" }); added++; }
                continue;
            }
            var existing = await db.AiConversationAnalyses.Where(a => a.TenantId == Tenant && a.ConversationId == chat.Id && a.WhatsAppAccountId == account).ToListAsync(ct);
            foreach (var stale in existing.Where(a => a.Fingerprint != source.Fingerprint && a.State != AiLearningState.Stale))
            {
                stale.State = AiLearningState.Stale; stale.Version++;
                await RetireDerivedAsync(stale.Id, ct);
            }
            if (existing.All(a => a.Fingerprint != source.Fingerprint))
            {
                db.Add(new AiConversationAnalysis { TenantId = Tenant, WhatsAppAccountId = account, ConversationId = chat.Id,
                    Fingerprint = source.Fingerprint, VerifiedOutcome = source.VerifiedOutcome, OutcomeEvidence = source.Evidence,
                    SalespersonId = source.SalespersonId, Cohort = source.Cohort,
                    IsEvaluationHoldout = existing.FirstOrDefault()?.IsEvaluationHoldout ??
                        Convert.ToInt32(AiLearningSourceService.Hash(chat.ContactId.ToString())[..4], 16) % 100 < settings.Value.HoldoutPercentage }); added++;
            }
        }
        config.Cursor = chats.Count < settings.Value.ScanBatchSize ? Guid.Empty : chats.Last().Id;
        config.LastScanAt = DateTime.UtcNow; config.Version++;
        await db.SaveChangesAsync(ct); return added;
    }
    public async Task ProcessAsync(Guid id, CancellationToken ct)
    {
        await using var gate = await ConversationLock.AcquireAsync(db, "learning-analysis:" + id, ct);
        var job = await db.AiConversationAnalyses.SingleOrDefaultAsync(a => a.Id == id && a.TenantId == Tenant, ct)
            ?? throw new AiSalesValidation("Analysis unavailable.");
        Require(job.WhatsAppAccountId, true);
        if (!await db.AiLearningAccounts.AnyAsync(a => a.TenantId == Tenant && a.WhatsAppAccountId == job.WhatsAppAccountId && !a.Paused, ct)) return;
        if (job.State is AiLearningState.Completed or AiLearningState.Stale || job.Attempts >= 5 || job.AvailableAt > DateTime.UtcNow) return;
        job.State = AiLearningState.Processing; job.Attempts++; job.Version++; await db.SaveChangesAsync(ct);
        try
        {
            var source = await sources.ReadAsync(job.WhatsAppAccountId, job.ConversationId, ct);
            if (source.Fingerprint != job.Fingerprint) { job.State = AiLearningState.Stale; await db.SaveChangesAsync(ct); return; }
            var chunks = job.InsightsJson == "{}" ? [] : JsonSerializer.Deserialize<List<AiLearningExtraction>>(job.InsightsJson, OpenAiLearningProvider.Json) ?? [];
            var sourceChunks = ChunkMessages(source.Messages).ToArray();
            if (chunks.Count > sourceChunks.Length) throw new AiSalesValidation("Invalid saved chunk receipts.");
            foreach (var chunk in sourceChunks.Skip(chunks.Count))
            {
                if (!await db.AiLearningAccounts.AnyAsync(a => a.TenantId == Tenant && a.WhatsAppAccountId == job.WhatsAppAccountId && !a.Paused, ct))
                    throw new AiSalesValidation("Learning paused.");
                var usage = await ReserveAsync("analysis", "configured-extractor", ct);
                try {
                    var response = await provider.AnalyseAsync(chunk, ct);
                    OpenAiLearningProvider.Validate(response.Extraction, chunk.Select(m => m.Id).ToHashSet());
                    chunks.Add(response.Extraction); usage.InputTokens = response.InputTokens; usage.OutputTokens = response.OutputTokens; usage.Status = "Completed";
                    if (!string.IsNullOrWhiteSpace(response.Model)) usage.Model = response.Model[..Math.Min(100, response.Model.Length)];
                } catch { usage.Status = "FailedOrInterrupted"; await db.SaveChangesAsync(CancellationToken.None); throw; }
                job.InsightsJson = JsonSerializer.Serialize(chunks, OpenAiLearningProvider.Json);
                await db.SaveChangesAsync(ct);
            }
            await using var chatGate = await ConversationLock.AcquireAsync(db, "conversation:" + job.ConversationId, ct);
            if ((await sources.ReadAsync(job.WhatsAppAccountId, job.ConversationId, ct)).Fingerprint != job.Fingerprint)
            { job.State = AiLearningState.Stale; await db.SaveChangesAsync(ct); return; }
            await using var accountGate = await ConversationLock.AcquireAsync(db, "knowledge-account:" + Tenant + ":" + job.WhatsAppAccountId, ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            job.InsightsJson = JsonSerializer.Serialize(chunks, OpenAiLearningProvider.Json);
            job.InferredStage = chunks.LastOrDefault()?.Stage ?? "NEW_LEAD"; job.Language = chunks.LastOrDefault()?.Language ?? "unknown";
            job.State = AiLearningState.Completed; job.CompletedAt = DateTime.UtcNow; job.ErrorCode = ""; job.Version++;
            foreach (var insight in (job.IsEvaluationHoldout ? [] : chunks.SelectMany(c => c.Insights).Take(30)))
            {
                var content = AiLearningSourceService.Redact(insight.Content, []);
                var hash = AiLearningSourceService.Hash(IntakeScript.Normalize(content));
                if (await db.AiKnowledgeIndexes.AnyAsync(i => i.TenantId == Tenant && i.WhatsAppAccountId == job.WhatsAppAccountId && i.ContentHash == hash, ct)) continue;
                var knowledge = new AiSalesKnowledge { TenantId = Tenant, WhatsAppAccountId = job.WhatsAppAccountId,
                    Kind = insight.Category is AiKnowledgeCategory.Business or AiKnowledgeCategory.Pricing or AiKnowledgeCategory.Faq ? AiKnowledgeKind.Fact : AiKnowledgeKind.Playbook,
                    Status = AiKnowledgeStatus.Draft, Title = AiLearningSourceService.Redact(insight.Title, []), Content = content,
                    SourceReference = $"Analysis {job.Id}; source {job.Fingerprint}; evidence {string.Join(',', insight.SourceMessageIds.Take(5))}",
                    ValidFrom = DateTime.UtcNow, ValidUntil = DateTime.UtcNow.AddDays(7) };
                db.Add(knowledge); db.Add(new AiKnowledgeIndex { KnowledgeId = knowledge.Id, TenantId = Tenant,
                    WhatsAppAccountId = job.WhatsAppAccountId, AnalysisId = job.Id, Category = insight.Category,
                    Confidence = insight.Confidence, ContentHash = hash, SourceFingerprint = job.Fingerprint });
                // Save inside the transaction so duplicate insights from the same extraction see the unique content.
                await db.SaveChangesAsync(ct);
            }
            Audit("AiLearningAnalysed", job.Id, null); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();
            var current = await db.AiConversationAnalyses.SingleOrDefaultAsync(a => a.Id == id && a.TenantId == Tenant, CancellationToken.None);
            if (current == null || current.State == AiLearningState.Stale) return;
            job = current;
            job.State = AiLearningState.Failed; job.ErrorCode = ex is AiSalesValidation ? ex.Message[..Math.Min(100, ex.Message.Length)] : ex.GetType().Name;
            job.AvailableAt = DateTime.UtcNow.AddMinutes(Math.Pow(2, job.Attempts)); job.Version++;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }
    public async Task<bool> SourceCurrentAsync(Guid? analysisId, CancellationToken ct)
    {
        if (analysisId == null) return false;
        var a = await db.AiConversationAnalyses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == analysisId && x.TenantId == Tenant, ct);
        if (a?.State != AiLearningState.Completed) return false;
        try { return (await sources.ReadAsync(a.WhatsAppAccountId, a.ConversationId, ct)).Fingerprint == a.Fingerprint; }
        catch (AiSalesValidation) { return false; }
    }
    public async Task ReviewAsync(Guid id, long version, bool approve, string title, string content,
        DateTime expires, bool privacyReviewed, Guid actor, CancellationToken ct)
    {
        var knowledge = await db.AiSalesKnowledge.SingleOrDefaultAsync(k => k.Id == id && k.TenantId == Tenant, ct)
            ?? throw new AiSalesValidation("Knowledge unavailable.");
        Require(knowledge.WhatsAppAccountId);
        var index = await db.AiKnowledgeIndexes.SingleAsync(i => i.KnowledgeId == id && i.TenantId == Tenant, ct);
        var sourceConversation = index.AnalysisId == null ? null : await db.AiConversationAnalyses.AsNoTracking()
            .Where(a => a.Id == index.AnalysisId && a.TenantId == Tenant).Select(a => (Guid?)a.ConversationId).SingleOrDefaultAsync(ct);
        await using var sourceGate = sourceConversation == null ? null : await ConversationLock.AcquireAsync(db, "conversation:" + sourceConversation.Value, ct);
        await using var gate = await ConversationLock.AcquireAsync(db, "knowledge-account:" + Tenant + ":" + knowledge.WhatsAppAccountId, ct);
        await db.Entry(knowledge).ReloadAsync(ct);
        if (knowledge.Version != version || knowledge.Status != AiKnowledgeStatus.Draft) throw new AiSalesConflict("Knowledge changed; refresh.");
        await db.Entry(index).ReloadAsync(ct);
        if (approve)
        {
            if (!privacyReviewed || string.IsNullOrWhiteSpace(title) || title.Length > 150 || string.IsNullOrWhiteSpace(content) || content.Length > 4000 ||
                expires.Kind == DateTimeKind.Unspecified || expires.ToUniversalTime() <= DateTime.UtcNow || expires.ToUniversalTime() > DateTime.UtcNow.AddDays(90))
                throw new AiSalesValidation("Review privacy, current facts, title/content and an expiry within 90 days.");
            if (index.AnalysisId != null && !await SourceCurrentAsync(index.AnalysisId, ct)) throw new AiSalesConflict("Source changed or removed; reprocess before approval.");
            var existingTitles = await db.AiSalesKnowledge.AsNoTracking().Where(k => k.TenantId == Tenant && k.WhatsAppAccountId == knowledge.WhatsAppAccountId &&
                k.Id != knowledge.Id && k.Kind == AiKnowledgeKind.Fact && k.Status == AiKnowledgeStatus.Approved && k.ValidFrom <= DateTime.UtcNow && k.ValidUntil > DateTime.UtcNow)
                .Select(k => k.Title).ToArrayAsync(ct);
            if (knowledge.Kind == AiKnowledgeKind.Fact && existingTitles.Any(t => IntakeScript.Normalize(t.Trim()) == IntakeScript.Normalize(title.Trim())))
                throw new AiSalesConflict("A current fact has the same title. Review and disable its obsolete version before approving a replacement; distinguish separate offers explicitly.");
            knowledge.Title = title.Trim(); knowledge.Content = content.Trim(); knowledge.ValidUntil = expires.ToUniversalTime();
            knowledge.Status = AiKnowledgeStatus.Approved; knowledge.ApprovedById = actor; knowledge.ApprovedAt = DateTime.UtcNow;
        }
        else knowledge.Status = AiKnowledgeStatus.Retired;
        knowledge.Version++; index.Embedding = []; index.KnowledgeVersion = 0;
        index.ContentHash = AiLearningSourceService.Hash(IntakeScript.Normalize(knowledge.Content)); index.UpdatedAt = DateTime.UtcNow;
        Audit(approve ? "AiLearningKnowledgeApproved" : "AiLearningKnowledgeRejected", id, actor); await db.SaveChangesAsync(ct);
    }
    public async Task RetireDerivedAsync(Guid analysisId, CancellationToken ct)
    {
        var ids = await db.AiKnowledgeIndexes.Where(i => i.TenantId == Tenant && i.AnalysisId == analysisId).Select(i => i.KnowledgeId).ToArrayAsync(ct);
        foreach (var k in await db.AiSalesKnowledge.Where(k => k.TenantId == Tenant && ids.Contains(k.Id)).ToListAsync(ct)) { k.Status = AiKnowledgeStatus.Retired; k.Version++; }
        foreach (var i in await db.AiKnowledgeIndexes.Where(i => i.TenantId == Tenant && ids.Contains(i.KnowledgeId)).ToListAsync(ct)) i.Embedding = [];
    }
    public void Audit(string action, Guid id, Guid? actor) => db.AuditLogs.Add(new AuditLog { Id = Guid.NewGuid(), ActorId = actor,
        ActorType = actor == null ? (byte)2 : (byte)1, EventType = action, EntityType = "AiSalesLearning", EntityId = id, CreatedAt = DateTime.UtcNow });
}
