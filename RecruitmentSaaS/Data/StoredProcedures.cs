using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace RecruitmentSaaS.Data;

// Calls the database functions that replaced the SQL Server stored procedures (migration/pg/04_routines.sql).
// Results carry the same values the SQL Server OUTPUT parameters did: a value the procedure never set is null.
public static class StoredProcedures
{
    public sealed record MoveToNextStageResult(bool? Success, string? Message, string? NewStageName);

    public static async Task<MoveToNextStageResult> MoveToNextStageAsync(this DatabaseFacade db,
        Guid candidateId, Guid movedById, string? notes, bool isOverride, string? overrideReason)
    {
        await using var cmd = await CreateCommandAsync(db,
            """SELECT "Success", "Message", "NewStageName" FROM demorecruitment."sp_MoveToNextStage"(@p_candidate_id, @p_moved_by_id, @p_notes, @p_is_override, @p_override_reason)""");
        Add(cmd, "p_candidate_id", NpgsqlDbType.Uuid, candidateId);
        Add(cmd, "p_moved_by_id", NpgsqlDbType.Uuid, movedById);
        Add(cmd, "p_notes", NpgsqlDbType.Citext, notes);
        Add(cmd, "p_is_override", NpgsqlDbType.Boolean, isOverride);
        Add(cmd, "p_override_reason", NpgsqlDbType.Citext, overrideReason);

        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return new MoveToNextStageResult(
            r.IsDBNull(0) ? null : r.GetBoolean(0),
            r.IsDBNull(1) ? null : r.GetString(1),
            r.IsDBNull(2) ? null : r.GetString(2));
    }

    public static async Task<Guid> ConvertLeadToCandidateAsync(this DatabaseFacade db,
        Guid leadId, Guid jobPackageId, Guid convertedById)
    {
        await using var cmd = await CreateCommandAsync(db,
            """SELECT "CandidateId" FROM demorecruitment."sp_ConvertLeadToCandidate"(@p_lead_id, @p_job_package_id, @p_converted_by_id)""");
        Add(cmd, "p_lead_id", NpgsqlDbType.Uuid, leadId);
        Add(cmd, "p_job_package_id", NpgsqlDbType.Uuid, jobPackageId);
        Add(cmd, "p_converted_by_id", NpgsqlDbType.Uuid, convertedById);
        try
        {
            return (Guid)(await cmd.ExecuteScalarAsync())!;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.RaiseException)
        {
            // SQL Server's RAISERROR surfaced as an exception whose Message was just the text; keep that
            // (Npgsql would prefix it with the error code, and the Sales page shows ex.Message to the user).
            throw new DatabaseProcedureException(ex.MessageText, ex);
        }
    }

    public sealed class DatabaseProcedureException(string message, Exception inner) : Exception(message, inner);

    // Runs on EF's own connection and inside EF's current transaction, if one is open.
    internal static async Task<DbCommand> CreateCommandAsync(DatabaseFacade db, string sql)
    {
        var conn = db.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await db.OpenConnectionAsync();
        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = db.CurrentTransaction?.GetDbTransaction();
        return cmd;
    }

    internal static void Add(DbCommand cmd, string name, NpgsqlDbType type, object? value) =>
        cmd.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });
}
