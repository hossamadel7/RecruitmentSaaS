#!/usr/bin/env bash
# Full SQL Server -> PostgreSQL migration, start to finish, ending with an exact row-by-row verification.
#
#   migration/migrate.sh <postgres-db-name> <sqlserver-db-name>
#
# Requires: the bacpac already imported into local SQL Server as <sqlserver-db-name>,
#           SSH key access to the OVH server, and the app role password in $PGPASS_FILE.
# WARNING: drops and recreates <postgres-db-name> on the server.
set -euo pipefail

PGDB="$1"
SQLDB="$2"
VPS="ubuntu@158.69.206.138"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PGPASS_FILE="${PGPASS_FILE:?set PGPASS_FILE to the file holding the recruitapp password}"
TOOL="$HERE/PgMigrator/bin/Release/net9.0/PgMigrator.dll"

export MIG_SQLSERVER="Server=localhost;Database=$SQLDB;Trusted_Connection=True;TrustServerCertificate=True"
export MIG_POSTGRES="Host=localhost;Port=15432;Database=$PGDB;Username=recruitapp;Password=$(cat "$PGPASS_FILE");Timeout=60;Command Timeout=600"

echo "==> [1/8] Build migration tool"
dotnet build "$HERE/PgMigrator" -c Release -nologo -v q | grep -E "error|Build succeeded"

echo "==> [2/8] SSH tunnel to PostgreSQL (localhost:15432)"
if ! (exec 3<>/dev/tcp/127.0.0.1/15432) 2>/dev/null; then
  ssh -o BatchMode=yes -o ExitOnForwardFailure=yes -f -N -L 15432:localhost:5432 "$VPS"
fi

echo "==> [3/8] Recreate database $PGDB"
ssh -o BatchMode=yes "$VPS" "sudo -n -u postgres psql -X -q -v ON_ERROR_STOP=1" <<SQL
SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = '$PGDB' AND pid <> pg_backend_pid();
DROP DATABASE IF EXISTS "$PGDB";
CREATE DATABASE "$PGDB" OWNER recruitapp TEMPLATE template0 ENCODING 'UTF8' LOCALE_PROVIDER icu ICU_LOCALE 'und' LOCALE 'C.UTF-8';
\c "$PGDB"
CREATE EXTENSION IF NOT EXISTS citext;
SQL

echo "==> [4/8] Create tables"
dotnet "$TOOL" schema "$HERE/pg"
"$HERE/run-sql.sh" "$PGDB" "$HERE/pg/01_tables.sql"

echo "==> [5/8] Copy data"
dotnet "$TOOL" copy

echo "==> [6/8] Constraints, indexes, foreign keys, functions, triggers, views, identity counters"
dotnet "$TOOL" identity "$HERE/pg"
"$HERE/run-sql.sh" "$PGDB" "$HERE/pg/03_constraints.sql" "$HERE/pg/04_routines.sql" "$HERE/pg/05_identity.sql" > /dev/null

echo "==> [7/8] Verify"
dotnet "$TOOL" verify

echo "==> [8/8] Record the PostgreSQL baseline EF migration"
"$HERE/run-sql.sh" "$PGDB" "$HERE/pg/06_ef_history.sql"
