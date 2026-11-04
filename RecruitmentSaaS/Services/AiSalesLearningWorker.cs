using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;

namespace RecruitmentSaaS.Services;
public sealed class AiSalesLearningWorker(IServiceScopeFactory scopes, IOptions<AiSalesLearningOptions> settings,
    ILogger<AiSalesLearningWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (settings.Value.Enabled && settings.Value.BackgroundEnabled)
            {
                try {
                    using var scope = scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<RecruitmentCrmContext>();
                    var learning = scope.ServiceProvider.GetRequiredService<AiSalesLearningService>();
                    await using var gate = await ConversationLock.AcquireAsync(db, "learning-worker:" + learning.Tenant, ct);
                    var accounts = await db.AiLearningAccounts.AsNoTracking().Where(a => a.TenantId == learning.Tenant && !a.Paused &&
                        settings.Value.ApprovedAccountIds.Contains(a.WhatsAppAccountId)).Select(a => a.WhatsAppAccountId).ToArrayAsync(ct);
                    foreach (var account in accounts)
                    {
                        await learning.ScanAsync(account, ct);
                        var jobs = await db.AiConversationAnalyses.AsNoTracking().Where(a => a.TenantId == learning.Tenant && a.WhatsAppAccountId == account &&
                            (a.State == AiLearningState.Pending || a.State == AiLearningState.Failed || a.State == AiLearningState.Processing) &&
                            a.Attempts < 5 && a.AvailableAt <= DateTime.UtcNow).OrderBy(a => a.CreatedAt).Select(a => a.Id).Take(5).ToArrayAsync(ct);
                        foreach (var id in jobs) await learning.ProcessAsync(id, ct);
                        if (settings.Value.SemanticRetrievalEnabled && settings.Value.ExternalProcessingApproved && settings.Value.PrivacyApproved)
                            await scope.ServiceProvider.GetRequiredService<AiSemanticKnowledgeService>().IndexAsync(account, ct);
                    }
                    // Erasure retires derived knowledge before deleting its index/source analysis. Never deletes CRM messages.
                    var cutoff = DateTime.UtcNow.AddDays(-settings.Value.RetentionDays);
                    foreach (var analysis in await db.AiConversationAnalyses.Where(a => a.TenantId == learning.Tenant && a.CreatedAt < cutoff).Take(50).ToListAsync(ct))
                    {
                        var knowledgeIds = await db.AiKnowledgeIndexes.Where(i => i.TenantId == learning.Tenant && i.AnalysisId == analysis.Id).Select(i => i.KnowledgeId).ToArrayAsync(ct);
                        db.AiSalesKnowledge.RemoveRange(await db.AiSalesKnowledge.Where(k => k.TenantId == learning.Tenant && knowledgeIds.Contains(k.Id)).ToListAsync(ct));
                        learning.Audit("AiLearningRetentionPurged", analysis.Id, null);
                        db.Remove(analysis);
                    }
                    var orphanIds = await db.AiKnowledgeIndexes.Where(i => i.TenantId == learning.Tenant && i.SourceFingerprint != "" && i.AnalysisId == null).Select(i => i.KnowledgeId).Take(50).ToArrayAsync(ct);
                    db.AiSalesKnowledge.RemoveRange(await db.AiSalesKnowledge.Where(k => k.TenantId == learning.Tenant && orphanIds.Contains(k.Id)).ToListAsync(ct));
                    await db.SaveChangesAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.LogError("Sales learning cycle failed ({ErrorType})", ex.GetType().Name); }
            }
            await Task.Delay(TimeSpan.FromSeconds(settings.Value.PollSeconds), ct);
        }
    }
}
