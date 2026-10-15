using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using RecruitmentSaaS.Data;
using RecruitmentSaaS.Models.Entities;
using System.Security.Cryptography;
using System.Text;

namespace RecruitmentSaaS.Services;

public sealed class DurableWorkService(RecruitmentCrmContext db)
{
    public async Task AcceptWebhookAsync(string payload, CancellationToken ct)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
        await using var gate = await ConversationLock.AcquireAsync(db, "webhook:" + hash, ct);
        var key = "webhook:" + hash;
        if (await db.DurableWorkItems.AnyAsync(w => w.DeduplicationKey == key, ct)) return;
        db.DurableWorkItems.Add(new() { Kind = "Webhook", DeduplicationKey = key, Payload = payload });
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>The work row is committed with the inbound message, before realtime notifications.</summary>
public sealed class AiIntakeQueue(RecruitmentCrmContext db)
{
    public void Enqueue(Guid conversationId, string messageId)
    {
        db.DurableWorkItems.Add(new() { Kind = "Intake", ConversationId = conversationId,
            DeduplicationKey = "intake:" + messageId, AvailableAt = DateTime.UtcNow.AddSeconds(4) });
    }
}

/// <summary>A bounded, single-consumer database queue. A cross-process session lock protects
/// processing; connection/process death releases it and leaves the durable item retryable.</summary>
public sealed class DurableWorkWorker(IServiceScopeFactory scopes, ILogger<DurableWorkWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunBatchAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError("Durable queue failed ({ErrorType})", ex.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    public async Task RunBatchAsync(CancellationToken ct)
    {
        using var lockScope = scopes.CreateScope();
        var lockDb = lockScope.ServiceProvider.GetRequiredService<RecruitmentCrmContext>();
        await using var gate = await ConversationLock.AcquireAsync(lockDb, "durable-worker", ct);
        var ids = await lockDb.DurableWorkItems.AsNoTracking()
            .Where(w => w.CompletedAt == null && w.Attempts < 8 && w.AvailableAt <= DateTime.UtcNow)
            .OrderBy(w => w.CreatedAt).ThenBy(w => w.Id).Select(w => w.Id).Take(20).ToListAsync(ct);
        foreach (var id in ids)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<RecruitmentCrmContext>();
            var work = await db.DurableWorkItems.SingleAsync(w => w.Id == id, ct);
            try
            {
                if (work.Kind == "Webhook")
                    await scope.ServiceProvider.GetRequiredService<IWhatsAppWebhookProcessor>().ProcessAsync(JObject.Parse(work.Payload), ct);
                else if (work.Kind == "Intake" && work.ConversationId != null)
                    await scope.ServiceProvider.GetRequiredService<AiIntakeService>().ReplyAsync(work.ConversationId.Value, ct);
                else throw new InvalidOperationException("Unknown durable work kind.");
                work.CompletedAt = DateTime.UtcNow; work.ErrorCode = null;
                // Erase successfully consumed webhook content; retain its hash/idempotency tombstone.
                work.Payload = "";
                await db.SaveChangesAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                db.ChangeTracker.Clear();
                work = await db.DurableWorkItems.SingleAsync(w => w.Id == id, ct);
                work.Attempts++;
                work.ErrorCode = ex.GetType().Name;
                work.AvailableAt = DateTime.UtcNow.AddSeconds(Math.Min(300, Math.Pow(2, work.Attempts)));
                await db.SaveChangesAsync(ct);
                logger.LogError("Durable work {WorkId} attempt {Attempt} failed ({ErrorType}); dead letter at 8 attempts", id, work.Attempts, work.ErrorCode);
            }
        }
    }
}

public sealed class AiIntakeTimeoutWorker(IServiceScopeFactory scopes, ILogger<AiIntakeTimeoutWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<RecruitmentCrmContext>();
                await using var gate = await ConversationLock.AcquireAsync(db, "intake-timeouts", ct);
                await scope.ServiceProvider.GetRequiredService<AiIntakeService>().SweepTimeoutsAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError("Intake timeout sweep failed ({ErrorType})", ex.GetType().Name); }
        }
    }
}
