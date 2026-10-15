#!/usr/bin/env bash
set -euo pipefail
umask 077
stage=${1:?private staging directory required}
[[ "$stage" =~ ^/home/ubuntu/\.local/share/recruitmentsaas-staging/[a-zA-Z0-9-]+$ ]] || exit 80
[[ -n "${2:-}" && "$(readlink /proc/self/ns/net)" != "$2" ]] || exit 81
bin=/usr/lib/postgresql/16/bin
app_pid=
cleanup() {
  if [[ -n "$app_pid" ]] && kill -0 "$app_pid" 2>/dev/null; then kill "$app_pid"; wait "$app_pid" || true; fi
  "$bin/pg_ctl" -D "$stage/pgdata" -m fast -w stop >> "$stage/postgres-stop.log" 2>&1 || true
  touch "$stage/stopped"
}
trap cleanup EXIT
socket="/tmp/recruitment-staging-${stage##*/}"
mkdir -p "$socket" "$stage/keys"
if [[ ! -f "$stage/pgdata/PG_VERSION" ]]; then
  "$bin/initdb" -D "$stage/pgdata" -U ubuntu -A trust --encoding=UTF8 --locale=C.UTF-8 --locale-provider=icu --icu-locale=und > "$stage/initdb.log" 2>&1
fi
"$bin/pg_ctl" -D "$stage/pgdata" -l "$stage/postgres.log" -o "-p 55441 -h 127.0.0.1 -k $socket" -w start
"$bin/createdb" -h 127.0.0.1 -p 55441 -U ubuntu validation_smoke
"$bin/pg_restore" -h 127.0.0.1 -p 55441 -U ubuntu -d validation_smoke --exit-on-error --single-transaction --no-owner --no-acl "$stage/RecruitmentCRM.dump" > "$stage/restore.log" 2>&1
"$bin/psql" -h 127.0.0.1 -p 55441 -U ubuntu -d validation_smoke -X -v ON_ERROR_STOP=1 -f "$stage/ai-sales-upgrade.sql" > "$stage/migration.log" 2>&1
touch "$stage/database-ready"
cd "$stage/app"
chmod +x RecruitmentSaaS
env ASPNETCORE_ENVIRONMENT=Staging ASPNETCORE_URLS=http://127.0.0.1:5191 DataProtection__KeysPath="$stage/keys" ./RecruitmentSaaS > "$stage/app.log" 2>&1 &
app_pid=$!
printf '%s\n' "$app_pid" > "$stage/app.pid"
for attempt in $(seq 1 60); do
  if curl --noproxy '*' -fsS http://127.0.0.1:5191/health > "$stage/health.txt"; then touch "$stage/app-ready"; break; fi
  kill -0 "$app_pid" 2>/dev/null || exit 82
  sleep 1
done
[[ -f "$stage/app-ready" ]] || exit 83
while [[ ! -f "$stage/stop" ]] && kill -0 "$app_pid" 2>/dev/null; do sleep 1; done
