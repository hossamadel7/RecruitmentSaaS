using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using RecruitmentSaaS.Data;

namespace RecruitmentSaaS.Services
{
    /// <summary>
    /// Deletes a staff user without losing business data. Every foreign key that points at Users
    /// is read from the database catalog, so links added later are handled too:
    ///  - the user's own records (notifications, sessions, sheet access, team form) are deleted;
    ///  - optional links are cleared (their leads go back to round-robin, chats become unassigned,
    ///    team members lose their manager, "approved by" fields are emptied);
    ///  - any required link on a business record (payments, candidates, documents, salaries, …)
    ///    blocks the delete — deactivate such users instead.
    /// </summary>
    public static class UserDeletion
    {
        // Required links that only hold the user's own data — deleted together with the user.
        private static readonly HashSet<string> OwnRecords = new(StringComparer.OrdinalIgnoreCase)
        {
            "Notifications.UserId",
            "RefreshTokens.UserId",
            "SalesGoogleSheetUsers.SalesUserId",
            "TeamLeadForms.ManagerId",
        };

        public sealed record Result(bool Deleted, string Message, int LeadsReturned = 0);

        private sealed record Link(string Table, string Column, bool Required)
        {
            public string Key => $"{Table.Replace("demorecruitment.", "").Trim('"')}.{Column}";
            public string Sql => $"{Table}.\"{Column}\"";
        }

        public static async Task<Result> DeleteAsync(RecruitmentCrmContext db, Guid userId, CancellationToken ct = default)
        {
            var conn = db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);

            var links = new List<Link>();
            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = """
                    select c.conrelid::regclass::text, a.attname, a.attnotnull
                    from pg_constraint c
                    join pg_attribute a on a.attrelid = c.conrelid and a.attnum = c.conkey[1]
                    where c.contype = 'f' and c.confrelid = 'demorecruitment."Users"'::regclass
                    """;
                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                    links.Add(new Link(r.GetString(0), r.GetString(1), r.GetBoolean(2)));
            }

            // 1. Anything that must not be lost blocks the delete
            var blockers = new List<string>();
            foreach (var link in links.Where(l => l.Required && !OwnRecords.Contains(l.Key)))
            {
                var n = await CountAsync(conn, link, userId, ct);
                if (n > 0) blockers.Add($"{link.Key.Split('.')[0]} ({n})");
            }
            if (blockers.Count > 0)
                return new Result(false, "لا يمكن حذف هذا المستخدم لأنه مرتبط بسجلات مهمة: "
                                         + string.Join("، ", blockers) + " — قم بتعطيله بدلاً من الحذف.");

            // 2. Unlink / remove, then delete the user — all or nothing
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var leadsReturned = await db.Database.ExecuteSqlRawAsync(
                """update demorecruitment."Leads" set "AssignedSalesId" = null, "UpdatedAt" = now() at time zone 'utc' where "AssignedSalesId" = {0} and not "IsConverted" """,
                new object[] { userId }, ct);

            foreach (var link in links)
            {
                var sql = link.Required
                    ? $"delete from {link.Table} where \"{link.Column}\" = {{0}}"   // only OwnRecords reach here with rows
                    : $"update {link.Table} set \"{link.Column}\" = null where \"{link.Column}\" = {{0}}";
                await db.Database.ExecuteSqlRawAsync(sql, new object[] { userId }, ct);
            }

            await db.Database.ExecuteSqlRawAsync("""delete from demorecruitment."Users" where "Id" = {0}""", new object[] { userId }, ct);
            await tx.CommitAsync(ct);

            return new Result(true, "", leadsReturned);
        }

        private static async Task<long> CountAsync(DbConnection conn, Link link, Guid userId, CancellationToken ct)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"select count(*) from {link.Table} where \"{link.Column}\" = @id";
            var p = cmd.CreateParameter();
            p.ParameterName = "id";
            p.Value = userId;
            cmd.Parameters.Add(p);
            return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
        }
    }
}
