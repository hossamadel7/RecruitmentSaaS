#!/usr/bin/env bash
# Runs the same business scenario on SQL Server and PostgreSQL (both rolled back) and diffs the outcomes.
#   migration/tests/compare.sh <sqlserver-db> <postgres-db>
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")"
sqlcmd -S localhost -E -C -I -d "$1" -y 0 -f 65001 -i behaviour.sqlserver.sql -o out.sqlserver.txt
sed -i 's/\r$//; s/[[:space:]]*$//; s/^\xEF\xBB\xBF//; /^$/d; /^line$/d; /^-*$/d; /rows affected/d' out.sqlserver.txt
ssh -o BatchMode=yes ubuntu@158.69.206.138 "sudo -n -u postgres psql -X -q -At -v ON_ERROR_STOP=1 -d '$2' -c 'SET ROLE recruitapp' -f -" < behaviour.postgres.sql > out.postgres.txt
sed -i '/^SET$/d' out.postgres.txt
echo "$(wc -l < out.sqlserver.txt) outcome lines compared"
diff out.sqlserver.txt out.postgres.txt && echo "IDENTICAL BEHAVIOUR"
