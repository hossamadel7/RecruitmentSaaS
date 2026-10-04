// PgMigrator — copies the RecruitmentCRM SQL Server database to PostgreSQL and proves the copy is exact.
//
//   PgMigrator schema   <out-dir>   generate 01_tables.sql + 03_constraints.sql from the SQL Server catalog
//   PgMigrator copy                 copy every row of every table (target tables must be empty)
//   PgMigrator identity <out-dir>   generate 05_identity.sql (identity counters exactly as SQL Server has them)
//   PgMigrator verify               compare every row/column, identity counters, views and object counts
//
// Connection strings come from MIG_SQLSERVER and MIG_POSTGRES.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Npgsql;
using NpgsqlTypes;

AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var mode = args.FirstOrDefault() ?? throw new ArgumentException("mode required: schema | copy | identity | verify");
var sqlCs = Environment.GetEnvironmentVariable("MIG_SQLSERVER") ?? throw new InvalidOperationException("Set MIG_SQLSERVER");

var catalog = await Catalog.LoadAsync(sqlCs);

switch (mode)
{
    case "schema":
        Directory.CreateDirectory(args[1]);
        var gen = new SchemaGenerator(catalog);
        await File.WriteAllTextAsync(Path.Combine(args[1], "01_tables.sql"), gen.Tables(), new UTF8Encoding(false));
        await File.WriteAllTextAsync(Path.Combine(args[1], "03_constraints.sql"), gen.Constraints(), new UTF8Encoding(false));
        Console.WriteLine($"Generated DDL for {catalog.Tables.Count} tables.");
        foreach (var w in gen.Warnings) Console.WriteLine("WARN " + w);
        break;
    case "copy":
        await Copier.CopyAsync(catalog, sqlCs, PgCs());
        break;
    case "identity":
        Directory.CreateDirectory(args[1]);
        await File.WriteAllTextAsync(Path.Combine(args[1], "05_identity.sql"), await SchemaGenerator.IdentitySql(catalog, sqlCs), new UTF8Encoding(false));
        Console.WriteLine("Generated identity counters.");
        break;
    case "verify":
        var ok = await Verifier.VerifyAsync(catalog, sqlCs, PgCs());
        return ok ? 0 : 1;
    default:
        throw new ArgumentException($"Unknown mode '{mode}'");
}
return 0;

static string PgCs() => Environment.GetEnvironmentVariable("MIG_POSTGRES") ?? throw new InvalidOperationException("Set MIG_POSTGRES");

// ───────────────────────────── catalog ─────────────────────────────

sealed record Column(int Id, string Name, string Type, int MaxLength, int Precision, int Scale, bool Nullable,
    bool IsIdentity, string? ComputedSql, string? DefaultName, string? DefaultSql)
{
    public bool IsComputed => ComputedSql != null;
}

sealed record IndexCol(string Name, bool Desc, bool Included);
sealed record Index(string Name, bool IsPk, bool IsUniqueConstraint, bool IsUnique, string? Filter, List<IndexCol> Cols);
sealed record Check(string Name, string Definition);
sealed record ForeignKey(string Name, List<string> Cols, string RefSchema, string RefTable, List<string> RefCols, string OnDelete, string OnUpdate);

sealed class Table
{
    public required int ObjectId;
    public required string Schema;
    public required string Name;
    public List<Column> Columns = new();
    public List<Index> Indexes = new();
    public List<Check> Checks = new();
    public List<ForeignKey> ForeignKeys = new();
    public string PgSchema => Schema == "dbo" ? "public" : Schema;
    public string SqlName => $"[{Schema}].[{Name}]";
    public string PgName => $"{Q(PgSchema)}.{Q(Name)}";
    public Index? Pk => Indexes.FirstOrDefault(i => i.IsPk);
    public static string Q(string id) => "\"" + id.Replace("\"", "\"\"") + "\"";
}

sealed class Catalog
{
    public List<Table> Tables = new();
    public List<(string Schema, string Name)> Views = new();
    public List<string> Schemas = new();

    public static async Task<Catalog> LoadAsync(string cs)
    {
        var c = new Catalog();
        await using var conn = new SqlConnection(cs);
        await conn.OpenAsync();

        var byId = new Dictionary<int, Table>();
        await Each(conn, "SELECT t.object_id, s.name, t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id WHERE t.is_ms_shipped=0 ORDER BY s.name, t.name", r =>
        {
            var t = new Table { ObjectId = r.GetInt32(0), Schema = r.GetString(1), Name = r.GetString(2) };
            c.Tables.Add(t); byId[t.ObjectId] = t;
        });

        await Each(conn, """
            SELECT c.object_id, c.column_id, c.name, ty.name, c.max_length, c.precision, c.scale, c.is_nullable, c.is_identity,
                   CAST(cc.definition AS nvarchar(4000)), dc.name, CAST(dc.definition AS nvarchar(4000))
            FROM sys.columns c
            JOIN sys.tables t ON t.object_id=c.object_id
            JOIN sys.types ty ON ty.user_type_id=c.user_type_id
            LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id
            LEFT JOIN sys.default_constraints dc ON dc.parent_object_id=c.object_id AND dc.parent_column_id=c.column_id
            ORDER BY c.object_id, c.column_id
            """, r =>
        {
            if (!byId.TryGetValue(r.GetInt32(0), out var t)) return;
            t.Columns.Add(new Column(r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetInt16(4), r.GetByte(5), r.GetByte(6),
                r.GetBoolean(7), r.GetBoolean(8), r.IsDBNull(9) ? null : r.GetString(9), r.IsDBNull(10) ? null : r.GetString(10), r.IsDBNull(11) ? null : r.GetString(11)));
        });

        var idx = new Dictionary<(int, int), Index>();
        await Each(conn, """
            SELECT i.object_id, i.index_id, i.name, i.is_primary_key, i.is_unique_constraint, i.is_unique, CAST(i.filter_definition AS nvarchar(4000)),
                   c.name, ic.is_descending_key, ic.is_included_column
            FROM sys.indexes i
            JOIN sys.tables t ON t.object_id=i.object_id
            JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
            JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
            WHERE i.type IN (1,2)
            ORDER BY i.object_id, i.index_id, ic.is_included_column, ic.key_ordinal, ic.index_column_id
            """, r =>
        {
            var key = (r.GetInt32(0), r.GetInt32(1));
            if (!idx.TryGetValue(key, out var ix))
            {
                ix = new Index(r.GetString(2), r.GetBoolean(3), r.GetBoolean(4), r.GetBoolean(5), r.IsDBNull(6) ? null : r.GetString(6), new());
                idx[key] = ix; byId[key.Item1].Indexes.Add(ix);
            }
            ix.Cols.Add(new IndexCol(r.GetString(7), r.GetBoolean(8), r.GetBoolean(9)));
        });

        await Each(conn, "SELECT parent_object_id, name, CAST(definition AS nvarchar(4000)) FROM sys.check_constraints ORDER BY parent_object_id, name", r =>
        {
            if (byId.TryGetValue(r.GetInt32(0), out var t)) t.Checks.Add(new Check(r.GetString(1), r.GetString(2)));
        });

        var fks = new Dictionary<int, ForeignKey>();
        await Each(conn, """
            SELECT fk.object_id, fk.parent_object_id, fk.name, rs.name, rt.name, pc.name, rc.name,
                   fk.delete_referential_action, fk.update_referential_action
            FROM sys.foreign_keys fk
            JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
            JOIN sys.tables rt ON rt.object_id=fk.referenced_object_id
            JOIN sys.schemas rs ON rs.schema_id=rt.schema_id
            JOIN sys.columns pc ON pc.object_id=fkc.parent_object_id AND pc.column_id=fkc.parent_column_id
            JOIN sys.columns rc ON rc.object_id=fkc.referenced_object_id AND rc.column_id=fkc.referenced_column_id
            ORDER BY fk.parent_object_id, fk.name, fkc.constraint_column_id
            """, r =>
        {
            var id = r.GetInt32(0);
            if (!fks.TryGetValue(id, out var fk))
            {
                fk = new ForeignKey(r.GetString(2), new(), r.GetString(3), r.GetString(4), new(), Action(r.GetByte(7)), Action(r.GetByte(8)));
                fks[id] = fk; byId[r.GetInt32(1)].ForeignKeys.Add(fk);
            }
            fk.Cols.Add(r.GetString(5)); fk.RefCols.Add(r.GetString(6));
        });

        await Each(conn, "SELECT s.name, v.name FROM sys.views v JOIN sys.schemas s ON s.schema_id=v.schema_id WHERE v.is_ms_shipped=0 ORDER BY 1,2",
            r => c.Views.Add((r.GetString(0), r.GetString(1))));
        c.Schemas = c.Tables.Select(t => t.PgSchema).Concat(c.Views.Select(v => v.Schema)).Distinct().OrderBy(s => s).ToList();
        return c;

        static string Action(byte a) => a switch { 0 => "NO ACTION", 1 => "CASCADE", 2 => "SET NULL", 3 => "SET DEFAULT", _ => throw new Exception("fk action " + a) };
    }

    public static async Task Each(SqlConnection conn, string sql, Action<SqlDataReader> row)
    {
        await using var cmd = new SqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) row(r);
    }
}

// ───────────────────────────── DDL generation ─────────────────────────────

sealed class SchemaGenerator(Catalog catalog)
{
    public List<string> Warnings = new();
    readonly Dictionary<string, HashSet<string>> _relNames = new();   // per schema: index / constraint-index names must be unique

    public static string PgType(Column c) => c.Type switch
    {
        "uniqueidentifier" => "uuid",
        "nvarchar" or "varchar" or "nchar" or "char" => "citext",
        "datetime2" => $"timestamp({Math.Min((int)c.Scale, 6)}) without time zone",   // datetime2(0) keeps whole seconds, as SQL Server does
        "datetime" or "smalldatetime" => "timestamp(6) without time zone",              // keeps the 1/300 s values exactly
        "date" => "date",
        "tinyint" or "smallint" => "smallint",
        "int" => "integer",
        "bigint" => "bigint",
        "bit" => "boolean",
        "decimal" or "numeric" => $"numeric({c.Precision},{c.Scale})",
        _ => throw new NotSupportedException($"type {c.Type}")
    };

    // Character limit as SQL Server enforces it (nvarchar max_length is in bytes).
    static int? CharLimit(Column c) => c.Type switch
    {
        "nvarchar" or "nchar" => c.MaxLength == -1 ? null : c.MaxLength / 2,
        "varchar" or "char" => c.MaxLength == -1 ? null : c.MaxLength,
        _ => null
    };

    public string Tables()
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- Generated by PgMigrator from the SQL Server catalog. Tables, columns, defaults, computed columns, primary keys.");
        sb.AppendLine("CREATE EXTENSION IF NOT EXISTS citext;");
        foreach (var s in catalog.Schemas.Where(s => s != "public")) sb.AppendLine($"CREATE SCHEMA IF NOT EXISTS {Table.Q(s)};");
        sb.AppendLine();
        foreach (var t in catalog.Tables)
        {
            var lines = new List<string>();
            foreach (var c in t.Columns)
            {
                var type = c.IsComputed ? ComputedType(t, c) : PgType(c);
                var line = $"    {Table.Q(c.Name)} {type}";
                if (c.IsComputed) line += $" GENERATED ALWAYS AS ({Computed(c.ComputedSql!)}) STORED";
                else if (c.IsIdentity) line += " GENERATED BY DEFAULT AS IDENTITY";
                else if (c.DefaultSql != null) line += $" CONSTRAINT {Table.Q(Trunc(c.DefaultName!))} DEFAULT {Default(c)}";
                if (!c.Nullable) line += " NOT NULL";
                lines.Add(line);
            }
            if (t.Pk is { } pk)
                lines.Add($"    CONSTRAINT {Table.Q(Unique(t.PgSchema, pk.Name))} PRIMARY KEY ({ColList(pk.Cols.Where(x => !x.Included))})");
            else Warnings.Add($"{t.SqlName} has no primary key");
            sb.AppendLine($"CREATE TABLE {t.PgName} (\n{string.Join(",\n", lines)}\n);");
        }
        return sb.ToString();
    }

    public string Constraints()
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- Generated by PgMigrator. Unique constraints, check constraints, length limits, indexes, foreign keys.");
        foreach (var t in catalog.Tables)
        {
            sb.AppendLine($"-- {t.SqlName}");
            foreach (var ix in t.Indexes.Where(i => i.IsUniqueConstraint))
                sb.AppendLine($"ALTER TABLE {t.PgName} ADD CONSTRAINT {Table.Q(Unique(t.PgSchema, ix.Name))} UNIQUE NULLS NOT DISTINCT ({ColList(ix.Cols)});");
            foreach (var ck in t.Checks)
                sb.AppendLine($"ALTER TABLE {t.PgName} ADD CONSTRAINT {Table.Q(Trunc(ck.Name))} CHECK {Expr(ck.Definition)};");
            foreach (var c in t.Columns.Where(c => !c.IsComputed))
                if (CharLimit(c) is int n)
                    sb.AppendLine($"ALTER TABLE {t.PgName} ADD CONSTRAINT {Table.Q(Trunc($"LEN_{t.Name}_{c.Name}"))} CHECK (char_length({Table.Q(c.Name)}) <= {n});");
            foreach (var ix in t.Indexes.Where(i => !i.IsPk && !i.IsUniqueConstraint))
            {
                var keys = ix.Cols.Where(x => !x.Included).Select(x => Table.Q(x.Name) + (x.Desc ? " DESC" : ""));
                var inc = ix.Cols.Where(x => x.Included).ToList();
                var unique = ix.IsUnique ? "UNIQUE " : "";
                var sql = $"CREATE {unique}INDEX {Table.Q(Unique(t.PgSchema, ix.Name))} ON {t.PgName} ({string.Join(", ", keys)})";
                if (inc.Count > 0) sql += $" INCLUDE ({ColList(inc)})";
                if (ix.IsUnique && ix.Filter == null) sql += " NULLS NOT DISTINCT";
                if (ix.Filter != null) sql += $" WHERE {Expr(ix.Filter)}";
                sb.AppendLine(sql + ";");
            }
        }
        sb.AppendLine();
        foreach (var t in catalog.Tables)
            foreach (var fk in t.ForeignKeys)
            {
                var refT = catalog.Tables.Single(x => x.Schema == fk.RefSchema && x.Name == fk.RefTable);
                sb.AppendLine($"ALTER TABLE {t.PgName} ADD CONSTRAINT {Table.Q(Trunc(fk.Name))} FOREIGN KEY ({string.Join(", ", fk.Cols.Select(Table.Q))}) " +
                              $"REFERENCES {refT.PgName} ({string.Join(", ", fk.RefCols.Select(Table.Q))}) ON DELETE {fk.OnDelete} ON UPDATE {fk.OnUpdate};");
            }
        return sb.ToString();
    }

    public static async Task<string> IdentitySql(Catalog catalog, string sqlCs)
    {
        var sb = new StringBuilder("-- Identity counters copied from SQL Server (IDENT_CURRENT), so the next value matches exactly.\n");
        await using var conn = new SqlConnection(sqlCs);
        await conn.OpenAsync();
        foreach (var t in catalog.Tables)
            foreach (var c in t.Columns.Where(c => c.IsIdentity))
            {
                await using var cmd = new SqlCommand($"SELECT CAST(IDENT_CURRENT('{t.SqlName}') AS bigint), (SELECT COUNT_BIG(*) FROM {t.SqlName}), CAST(IDENT_SEED('{t.SqlName}') AS bigint)", conn);
                await using var r = await cmd.ExecuteReaderAsync();
                await r.ReadAsync();
                var current = r.GetInt64(0); var rows = r.GetInt64(1); var seed = r.GetInt64(2);
                // A never-used identity reports IDENT_CURRENT = seed, and its next value is the seed itself.
                var isCalled = rows > 0 || current != seed ? "true" : "false";
                sb.AppendLine($"SELECT setval(pg_get_serial_sequence('{t.PgName.Replace("'", "''")}', '{c.Name}'), {current}, {isCalled});");
            }
        return sb.ToString();
    }

    string ComputedType(Table t, Column c) => c.ComputedSql switch
    {
        _ when c.Name == "LeadCode" => "citext",
        _ when c.Name == "TotalAmount" => $"numeric({c.Precision},{c.Scale})",
        _ => throw new NotSupportedException($"computed column {t.SqlName}.{c.Name}")
    };

    static string Computed(string def) => def switch
    {
        "('LD-'+right('00000'+CONVERT([nvarchar](10),[LeadSequence]),(5)))" => "('LD-' || right('00000' || (\"LeadSequence\")::text, 5))::citext",
        "([BaseSalary]+[Adjustment])" => "(\"BaseSalary\" + \"Adjustment\")",
        _ => throw new NotSupportedException("computed " + def)
    };

    static string Default(Column c)
    {
        var d = c.DefaultSql!;
        string? lit = d switch
        {
            "(newsequentialid())" or "(newid())" => "gen_random_uuid()",
            "(sysutcdatetime())" or "(getutcdate())" => "(clock_timestamp() AT TIME ZONE 'utc')",
            _ => null
        };
        if (lit != null) return lit;
        var m = Regex.Match(d, @"^\((?:CONVERT\(\[(?:tinyint|bit)\],)?\(*(-?[0-9.]+)\)*\)$");
        if (m.Success)
        {
            var num = m.Groups[1].Value.TrimEnd('.');
            if (c.Type == "bit") return num == "1" ? "true" : num == "0" ? "false" : throw new NotSupportedException(d);
            return num;
        }
        m = Regex.Match(d, @"^\(N?'((?:[^']|'')*)'\)$");
        if (m.Success) return $"'{m.Groups[1].Value}'";
        throw new NotSupportedException($"default {d} on {c.Name}");
    }

    // Check/filter expressions: column brackets → quoted identifiers, N'..' → '..'. Everything else in this catalog is portable SQL.
    static string Expr(string e)
    {
        var s = Regex.Replace(e, @"\[([^\]]+)\]", m => Table.Q(m.Groups[1].Value));
        s = Regex.Replace(s, @"\bN'", "'");
        if (Regex.IsMatch(s, @"\b(CONVERT|ISNULL|LEN|GETDATE|DATEADD)\b", RegexOptions.IgnoreCase)) throw new NotSupportedException("expr " + e);
        return s.StartsWith('(') ? s : $"({s})";
    }

    string Unique(string schema, string name)
    {
        var set = _relNames.TryGetValue(schema, out var s) ? s : _relNames[schema] = new(StringComparer.Ordinal);
        var n = Trunc(name);
        for (var i = 2; !set.Add(n); i++) { n = Trunc(name, 58) + "_" + i; Warnings.Add($"renamed duplicate name {schema}.{name} -> {n}"); }
        return n;
    }

    static string Trunc(string n, int max = 63) => Encoding.UTF8.GetByteCount(n) <= max ? n : n[..max];
    static string ColList(IEnumerable<IndexCol> cols) => string.Join(", ", cols.Select(c => Table.Q(c.Name)));
}

// ───────────────────────────── data copy ─────────────────────────────

static class Copier
{
    public static async Task CopyAsync(Catalog catalog, string sqlCs, string pgCs)
    {
        await using var src = new SqlConnection(sqlCs);
        await src.OpenAsync();
        await using var dst = new NpgsqlConnection(pgCs);
        await dst.OpenAsync();
        await using var tx = await dst.BeginTransactionAsync();   // all tables or nothing

        long total = 0;
        foreach (var t in catalog.Tables)
        {
            await using (var chk = new NpgsqlCommand($"SELECT count(*) FROM {t.PgName}", dst, tx))
                if ((long)(await chk.ExecuteScalarAsync())! != 0) throw new Exception($"{t.PgName} is not empty — refusing to copy");

            var cols = t.Columns.Where(c => !c.IsComputed).ToList();
            var select = $"SELECT {string.Join(", ", cols.Select(c => $"[{c.Name}]"))} FROM {t.SqlName}";
            var copy = $"COPY {t.PgName} ({string.Join(", ", cols.Select(c => Table.Q(c.Name)))}) FROM STDIN (FORMAT BINARY)";

            await using var cmd = new SqlCommand(select, src);
            await using var r = await cmd.ExecuteReaderAsync();
            long n = 0;
            await using (var w = await dst.BeginBinaryImportAsync(copy))
            {
                while (await r.ReadAsync())
                {
                    await w.StartRowAsync();
                    for (var i = 0; i < cols.Count; i++)
                    {
                        if (r.IsDBNull(i)) { await w.WriteNullAsync(); continue; }
                        var c = cols[i];
                        switch (c.Type)
                        {
                            case "uniqueidentifier": await w.WriteAsync(r.GetGuid(i), NpgsqlDbType.Uuid); break;
                            case "nvarchar" or "varchar" or "nchar" or "char":
                                var s = r.GetString(i);
                                if (s.Contains('\0')) throw new Exception($"{t.SqlName}.{c.Name} contains a NUL character, which PostgreSQL cannot store");
                                await w.WriteAsync(s, NpgsqlDbType.Citext); break;
                            case "datetime2" or "datetime" or "smalldatetime": await w.WriteAsync(r.GetDateTime(i), NpgsqlDbType.Timestamp); break;
                            case "date": await w.WriteAsync(r.GetDateTime(i), NpgsqlDbType.Date); break;
                            case "tinyint": await w.WriteAsync((short)r.GetByte(i), NpgsqlDbType.Smallint); break;
                            case "smallint": await w.WriteAsync(r.GetInt16(i), NpgsqlDbType.Smallint); break;
                            case "int": await w.WriteAsync(r.GetInt32(i), NpgsqlDbType.Integer); break;
                            case "bigint": await w.WriteAsync(r.GetInt64(i), NpgsqlDbType.Bigint); break;
                            case "bit": await w.WriteAsync(r.GetBoolean(i), NpgsqlDbType.Boolean); break;
                            case "decimal" or "numeric": await w.WriteAsync(r.GetDecimal(i), NpgsqlDbType.Numeric); break;
                            default: throw new NotSupportedException(c.Type);
                        }
                    }
                    n++;
                }
                await w.CompleteAsync();
            }
            total += n;
            Console.WriteLine($"  {t.SqlName,-55} {n,8} rows");
        }
        await tx.CommitAsync();
        Console.WriteLine($"Copied {total} rows across {catalog.Tables.Count} tables.");
    }
}

// ───────────────────────────── verification ─────────────────────────────

static class Verifier
{
    public static async Task<bool> VerifyAsync(Catalog catalog, string sqlCs, string pgCs)
    {
        await using var src = new SqlConnection(sqlCs);
        await src.OpenAsync();
        await using var dst = new NpgsqlConnection(pgCs);
        await dst.OpenAsync();
        var failures = 0; long rowsChecked = 0, cellsChecked = 0, subMicro = 0;

        Console.WriteLine("── Rows: every column of every row, matched by primary key");
        foreach (var t in catalog.Tables)
        {
            var pk = t.Pk!.Cols.Select(c => c.Name).ToList();
            var cols = t.Columns.Select(c => c.Name).ToList();
            var a = await ReadSql(src, $"SELECT {string.Join(", ", cols.Select(c => $"[{c}]"))} FROM {t.SqlName}", cols, pk, t, v => subMicro += v);
            var b = await ReadPg(dst, $"SELECT {string.Join(", ", cols.Select(Table.Q))} FROM {t.PgName}", cols, pk);
            var bad = new List<string>();
            if (a.Count != b.Count) bad.Add($"row count {a.Count} vs {b.Count}");
            foreach (var (k, row) in a)
            {
                if (!b.TryGetValue(k, out var other)) { bad.Add($"missing row {k}"); continue; }
                for (var i = 0; i < cols.Count; i++)
                    if (row[i] != other[i]) bad.Add($"{k}.{cols[i]}: '{row[i]}' vs '{other[i]}'");
            }
            foreach (var k in b.Keys.Where(k => !a.ContainsKey(k))) bad.Add($"extra row {k}");
            rowsChecked += a.Count; cellsChecked += a.Count * cols.Count;
            Console.WriteLine($"  {(bad.Count == 0 ? "OK  " : "FAIL")} {t.SqlName,-55} {a.Count,6} rows");
            foreach (var x in bad.Take(10)) Console.WriteLine("       " + x);
            if (bad.Count > 0) failures++;
        }

        Console.WriteLine("── Identity counters (next generated value)");
        foreach (var t in catalog.Tables)
            foreach (var c in t.Columns.Where(c => c.IsIdentity))
            {
                await using var s1 = new SqlCommand($"SELECT CAST(IDENT_CURRENT('{t.SqlName}') AS bigint) + CASE WHEN (SELECT COUNT(*) FROM {t.SqlName})=0 AND IDENT_CURRENT('{t.SqlName}')=IDENT_SEED('{t.SqlName}') THEN 0 ELSE IDENT_INCR('{t.SqlName}') END", src);
                var nextSql = Convert.ToInt64(await s1.ExecuteScalarAsync());
                await using var s0 = new NpgsqlCommand($"SELECT pg_get_serial_sequence('{t.PgName.Replace("'", "''")}', '{c.Name}')", dst);
                var seq = (string)(await s0.ExecuteScalarAsync())!;
                await using var s2 = new NpgsqlCommand($"SELECT CASE WHEN is_called THEN last_value + 1 ELSE last_value END FROM {seq}", dst);
                var nextPg = Convert.ToInt64(await s2.ExecuteScalarAsync());
                var ok = nextSql == nextPg;
                if (!ok) failures++;
                Console.WriteLine($"  {(ok ? "OK  " : "FAIL")} {t.SqlName}.{c.Name}: next {nextSql} vs {nextPg}");
            }

        Console.WriteLine("── Views: identical result sets");
        foreach (var (schema, view) in catalog.Views)
        {
            var cols = new List<string>();
            await Catalog.Each(src, $"SELECT c.name FROM sys.columns c WHERE c.object_id = OBJECT_ID('[{schema}].[{view}]') ORDER BY c.column_id", r => cols.Add(r.GetString(0)));
            var a = (await ReadSql(src, $"SELECT {string.Join(", ", cols.Select(c => $"[{c}]"))} FROM [{schema}].[{view}]", cols, cols, null, _ => { })).Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();
            var b = (await ReadPg(dst, $"SELECT {string.Join(", ", cols.Select(Table.Q))} FROM {Table.Q(schema)}.{Table.Q(view)}", cols, cols)).Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();
            var ok = a.SequenceEqual(b);
            if (!ok) failures++;
            Console.WriteLine($"  {(ok ? "OK  " : "FAIL")} {schema}.{view} ({a.Count} rows)");
        }

        Console.WriteLine("── Object counts");
        async Task<long> PgCount(string sql) { await using var c = new NpgsqlCommand(sql, dst); return Convert.ToInt64(await c.ExecuteScalarAsync()); }
        async Task<long> SqlCount(string sql) { await using var c = new SqlCommand(sql, src); return Convert.ToInt64(await c.ExecuteScalarAsync()); }
        const string pgUser = "n.nspname NOT IN ('pg_catalog','information_schema') AND n.nspname NOT LIKE 'pg_toast%'";
        var checks = new (string What, Func<Task<long>> Sql, Func<Task<long>> Pg)[]
        {
            ("tables", () => SqlCount("SELECT COUNT(*) FROM sys.tables WHERE is_ms_shipped=0"), () => PgCount($"SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE c.relkind='r' AND {pgUser}")),
            ("columns", () => SqlCount("SELECT COUNT(*) FROM sys.columns c JOIN sys.tables t ON t.object_id=c.object_id"), () => PgCount($"SELECT count(*) FROM pg_attribute a JOIN pg_class c ON c.oid=a.attrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE c.relkind='r' AND a.attnum>0 AND NOT a.attisdropped AND {pgUser}")),
            ("primary keys", () => SqlCount("SELECT COUNT(*) FROM sys.key_constraints WHERE type='PK'"), () => PgCount($"SELECT count(*) FROM pg_constraint k JOIN pg_namespace n ON n.oid=k.connamespace WHERE k.contype='p' AND {pgUser}")),
            ("foreign keys", () => SqlCount("SELECT COUNT(*) FROM sys.foreign_keys"), () => PgCount($"SELECT count(*) FROM pg_constraint k JOIN pg_namespace n ON n.oid=k.connamespace WHERE k.contype='f' AND {pgUser}")),
            ("unique constraints", () => SqlCount("SELECT COUNT(*) FROM sys.key_constraints WHERE type='UQ'"), () => PgCount($"SELECT count(*) FROM pg_constraint k JOIN pg_namespace n ON n.oid=k.connamespace WHERE k.contype='u' AND {pgUser}")),
            ("check constraints (excl. length limits)", () => SqlCount("SELECT COUNT(*) FROM sys.check_constraints"), () => PgCount($"SELECT count(*) FROM pg_constraint k JOIN pg_namespace n ON n.oid=k.connamespace WHERE k.contype='c' AND k.conname NOT LIKE 'LEN\\_%' AND {pgUser}")),
            ("defaults (incl. identity/computed)", () => SqlCount("SELECT (SELECT COUNT(*) FROM sys.default_constraints)+(SELECT COUNT(*) FROM sys.identity_columns i JOIN sys.tables t ON t.object_id=i.object_id)+(SELECT COUNT(*) FROM sys.computed_columns c JOIN sys.tables t ON t.object_id=c.object_id)"), () => PgCount($"SELECT count(*) FROM pg_attribute a JOIN pg_class c ON c.oid=a.attrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE c.relkind='r' AND a.attnum>0 AND (a.atthasdef OR a.attidentity<>'') AND {pgUser}")),
            ("table indexes", () => SqlCount("SELECT COUNT(*) FROM sys.indexes i JOIN sys.tables t ON t.object_id=i.object_id WHERE i.type IN (1,2)"), () => PgCount($"SELECT count(*) FROM pg_index x JOIN pg_class c ON c.oid=x.indrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE c.relkind='r' AND {pgUser}")),
            ("views", () => SqlCount("SELECT COUNT(*) FROM sys.views WHERE is_ms_shipped=0"), () => PgCount($"SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE c.relkind='v' AND {pgUser}")),
            ("procedures → functions", () => SqlCount("SELECT COUNT(*) FROM sys.procedures WHERE is_ms_shipped=0"), () => PgCount($"SELECT count(*) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE p.proname LIKE 'sp\\_%' AND {pgUser}")),
            ("triggers", () => SqlCount("SELECT COUNT(*) FROM sys.triggers WHERE parent_class=1"), () => PgCount($"SELECT count(*) FROM pg_trigger g JOIN pg_class c ON c.oid=g.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE NOT g.tgisinternal AND {pgUser}")),
        };
        foreach (var (what, sqlT, pgT) in checks)
        {
            long a = await sqlT(), b = await pgT();
            var ok = a == b;
            if (!ok) failures++;
            Console.WriteLine($"  {(ok ? "OK  " : "FAIL")} {what,-42} {a,5} vs {b,5}");
        }

        Console.WriteLine();
        Console.WriteLine($"Checked {rowsChecked} rows / {cellsChecked} values.");
        if (subMicro > 0) Console.WriteLine($"Note: {subMicro} datetime2 values carry a 7th fractional digit (100 ns); PostgreSQL stores microseconds, so that digit is truncated. Compared at microsecond precision.");
        Console.WriteLine(failures == 0 ? "VERIFY PASSED — PostgreSQL matches SQL Server exactly." : $"VERIFY FAILED — {failures} problem(s).");
        return failures == 0;
    }

    static async Task<Dictionary<string, string[]>> ReadSql(SqlConnection conn, string sql, List<string> cols, List<string> key, Table? t, Action<long> subMicro)
    {
        var rows = new Dictionary<string, string[]>();
        await using var cmd = new SqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync();
        long sub = 0;
        while (await r.ReadAsync())
        {
            var v = new string[cols.Count];
            for (var i = 0; i < cols.Count; i++)
            {
                var o = r.GetValue(i);
                if (o is DateTime dt && dt.Ticks % 10 != 0) sub++;
                v[i] = Canon(o);
            }
            Add(rows, key.Count == cols.Count ? string.Join("|", v) : Key(v, cols, key), v);
        }
        subMicro(sub);
        return rows;
    }

    static async Task<Dictionary<string, string[]>> ReadPg(NpgsqlConnection conn, string sql, List<string> cols, List<string> key)
    {
        var rows = new Dictionary<string, string[]>();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var v = new string[cols.Count];
            for (var i = 0; i < cols.Count; i++) v[i] = Canon(r.GetValue(i));
            Add(rows, key.Count == cols.Count ? string.Join("|", v) : Key(v, cols, key), v);
        }
        return rows;
    }

    // View rows have no key, so identical rows are numbered to keep duplicates distinct.
    static void Add(Dictionary<string, string[]> rows, string k, string[] v)
    {
        var key = k;
        for (var n = 2; rows.ContainsKey(key); n++) key = k + "#" + n;
        rows[key] = v;
    }

    static string Key(string[] v, List<string> cols, List<string> key) => string.Join("|", key.Select(k => v[cols.IndexOf(k)]));

    static string Canon(object o) => o switch
    {
        DBNull => "<null>",
        Guid g => g.ToString("D"),
        DateTime d => (d.Ticks / 10).ToString(),                    // microseconds
        decimal m => (m / 1.000000000000000000000000000000000m).ToString(CultureInfo.InvariantCulture),
        bool b => b ? "1" : "0",
        byte or short or int or long => Convert.ToInt64(o).ToString(),
        string s => "s:" + s,
        _ => throw new NotSupportedException(o.GetType().Name)
    };
}
