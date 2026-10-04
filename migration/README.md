# SQL Server → PostgreSQL migration

Moves the Azure SQL database (`RecruitmentCRM`) to PostgreSQL 16 on the OVH server and proves the copy is exact.

## What is where

| Path | What |
|---|---|
| `migrate.sh` | The whole migration, start to finish, ending with verification. **Drops and recreates the target database.** |
| `PgMigrator/` | Tool: generates the PostgreSQL tables from the SQL Server catalog, copies every row, verifies. |
| `pg/01_tables.sql`, `pg/03_constraints.sql` | Generated DDL (tables, defaults, computed columns, keys, checks, indexes, foreign keys). |
| `pg/04_routines.sql` | Hand-ported stored procedures → functions, triggers, views. |
| `pg/05_identity.sql` | Generated: identity counters set to SQL Server's exact current values. |
| `pg/06_ef_history.sql` | Marks the `PostgresBaseline` EF migration as applied. |
| `source/modules.sql` | The original SQL Server procedure/trigger/view source, for reference. |
| `tests/compare.sh` | Runs the same business scenario on both databases (rolled back) and diffs the outcome. |
| `run-sql.sh` | Runs SQL files on the server as the app's database owner. |

## How behaviour is kept identical

- **Case-insensitive text** — SQL Server used `SQL_Latin1_General_CP1_CI_AS`, so emails, phones and names matched
  regardless of case. Every text column is `citext`, and the app sends string parameters as `citext`.
- **Unique constraints treat NULLs as equal** (as SQL Server does) — `NULLS NOT DISTINCT`.
- **Text length limits** (`nvarchar(n)`) are enforced with `CHECK (char_length(col) <= n)`.
- **Timestamps keep their precision** — `datetime2(0)` → `timestamp(0)` (whole seconds), `datetime` → `timestamp(6)`
  (stores the 1/300 s values exactly). The app keeps writing UTC values unchanged (`EnableLegacyTimestampBehavior`).
- **Identity counters** (`Leads.LeadSequence`) continue from SQL Server's exact next value, so `LD-xxxxx` codes continue.
- **Procedures** return the same messages (GUIDs upper-case like SQL Server's text conversion), roll back the same
  way on error, and raise the same error texts. **Triggers** are statement-level with an `inserted` transition table.
- **Views** return the same column types the app's view models expect.

Only loss: `datetime2(7)` values carrying a 7th fractional digit (100 ns) keep microseconds (PostgreSQL's limit).

## Run it

1. Export a fresh `.bacpac` from Azure and import it locally:
   ```bash
   MSYS_NO_PATHCONV=1 DOTNET_ROLL_FORWARD=Major ~/.dotnet/tools/sqlpackage.exe /a:Import "/sf:C:\path\to\export.bacpac" /tsn:localhost /tdn:RecruitmentCRM_Final /ttsc:true
   ```
2. Migrate and verify (`PGPASS_FILE` = a local file holding the `recruitapp` password):
   ```bash
   PGPASS_FILE=/path/to/pgpass.txt migration/migrate.sh RecruitmentCRM RecruitmentCRM_Final
   ```
   It must end with `VERIFY PASSED`.
3. Optionally prove behaviour on the copy (everything is rolled back):
   ```bash
   migration/tests/compare.sh RecruitmentCRM_Final RecruitmentCRM
   ```
