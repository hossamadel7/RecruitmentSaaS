using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace RecruitmentSaaS.Services;

/// <summary>Session advisory lock: no DB transaction is kept open during provider calls.
/// All conversational writers use this same key. Release before disposing the DbContext.</summary>
public sealed class ConversationLock : IAsyncDisposable
{
    private readonly RecruitmentCrmContext db;
    private readonly long key;
    private ConversationLock(RecruitmentCrmContext db, long key) { this.db = db; this.key = key; }
    public static Task LockTransactionAsync(RecruitmentCrmContext db, string resource, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction == null) throw new InvalidOperationException("A transaction must own the rotation lock.");
        var key = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes("crm:" + resource)));
        return db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
    }
    public static async Task<ConversationLock> AcquireAsync(RecruitmentCrmContext db, string resource, CancellationToken ct = default)
    {
        var key = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes("crm:" + resource)));
        await db.Database.OpenConnectionAsync(ct);
        try { await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_lock({key})", ct); }
        catch { await db.Database.CloseConnectionAsync(); throw; }
        return new(db, key);
    }
    public async ValueTask DisposeAsync()
    {
        try { await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_unlock({key})"); }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}
