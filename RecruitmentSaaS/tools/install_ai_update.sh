#!/usr/bin/env bash
# Run only after the owner's explicit live-installation approval.
set -euo pipefail
umask 077
root=${1:?private installation directory required}
[[ "$root" =~ ^/home/ubuntu/\.local/share/recruitmentsaas-installations/[a-zA-Z0-9-]+$ ]] || exit 80
[[ $EUID -eq 0 ]] || exit 81
app=/var/www/recruitmentsaas
next="/var/www/recruitmentsaas-releases/${root##*/}-new"
previous="/var/www/recruitmentsaas-releases/${root##*/}-previous"
[[ -d "$app" && ! -L "$app" && ! -e "$next" && ! -e "$previous" ]] || exit 82
expected_archive=DFB57A55E0915C83B4788323D0E238A05D9747D46621611DFEEFD9CC048B32F5
expected_sql=212AF44C5E1D276A66DD446B50011C9EED75F4B02C8FD8B9871C05B1701CD9EE
expected_dll=0081B931D72E7239CD2C4B713925C8C729023E5C455657557740D4F8623EF3B5
[[ "$(sha256sum "$root/application-no-config.tar.gz" | cut -d' ' -f1)" == "${expected_archive,,}" ]] || exit 83
[[ "$(sha256sum "$root/ai-sales-upgrade.sql" | cut -d' ' -f1)" == "${expected_sql,,}" ]] || exit 84
systemctl is-active --quiet recruitmentsaas
curl --noproxy '*' -fsS --max-time 5 http://127.0.0.1:8082/health > "$root/health.before"
cp -a /etc/recruitmentsaas/app.env "$root/app.env.before"
systemctl cat recruitmentsaas > "$root/service.before"
cp -a /var/lib/recruitmentsaas/keys "$root/keys.before"
chmod 600 "$root/app.env.before" "$root/service.before"
mkdir -p /var/www/recruitmentsaas-releases "$next"
cp -a "$app/." "$next/"
service_stopped=0
app_moved=0
installed=0
rollback() {
  status=$?
  if [[ $installed -eq 0 && $service_stopped -eq 1 ]]; then
    systemctl stop recruitmentsaas || true
    if [[ $app_moved -eq 1 ]]; then
      if [[ -d "$app" && ! -L "$app" ]]; then mv "$app" "$root/failed-application"; fi
      mv "$previous" "$app"
    fi
    cp -a "$root/app.env.before" /etc/recruitmentsaas/app.env
    systemctl start recruitmentsaas || true
    printf '{"installationSucceeded":false,"codeRollbackAttempted":true,"databaseDownMigrationPerformed":false}\n' > "$root/result.json"
  elif [[ $installed -eq 0 ]]; then
    printf '{"installationSucceeded":false,"productionServiceStopped":false}\n' > "$root/result.json"
  fi
  exit "$status"
}
trap rollback EXIT
printf 'Stopping service for the approved installation.\n'
systemctl stop recruitmentsaas
service_stopped=1
# Capture a quiescent source application and database, including uploads/configuration.
cp -a "$app/." "$next/"
runuser -u postgres -- /usr/lib/postgresql/16/bin/pg_dump --format=custom --no-owner --no-acl --lock-wait-timeout=10s RecruitmentCRM > "$root/RecruitmentCRM-before.dump"
sha256sum "$root/RecruitmentCRM-before.dump" > "$root/database-backup.sha256"
python3 "$root/deployment_data_check.py" before "$root"
env PGOPTIONS='-c lock_timeout=10s -c statement_timeout=120s' runuser -u postgres -- /usr/lib/postgresql/16/bin/psql -X -v ON_ERROR_STOP=1 -d RecruitmentCRM < "$root/ai-sales-upgrade.sql" > "$root/migration.log" 2>&1
python3 "$root/deployment_data_check.py" after "$root"
python3 - "$root/application-no-config.tar.gz" "$next" <<'PY'
import pathlib,tarfile,sys
base=pathlib.Path(sys.argv[2]).resolve()
with tarfile.open(sys.argv[1]) as archive:
 for member in archive.getmembers():
  path=(base/member.name).resolve()
  if not path.is_relative_to(base) or not member.isfile():
   raise SystemExit('Archive path/type validation failed')
  if pathlib.Path(member.name).name.startswith('appsettings') or member.name.startswith('wwwroot/uploads/'):
   raise SystemExit('Configuration/upload files must not be replaced')
PY
umask 022
tar --no-same-owner -xzf "$root/application-no-config.tar.gz" -C "$next"
umask 077
python3 - "$root/application-no-config.tar.gz" "$next" <<'PY'
import pathlib,tarfile,sys,os
base=pathlib.Path(sys.argv[2]).resolve()
with tarfile.open(sys.argv[1]) as archive:
 for member in archive.getmembers():
  os.chmod(base/member.name,0o755 if member.name=='RecruitmentSaaS' else 0o644)
PY
chown --reference="$app" "$next"
chmod --reference="$app" "$next"
[[ "$(sha256sum "$next/RecruitmentSaaS.dll" | cut -d' ' -f1)" == "${expected_dll,,}" ]] || exit 85
chmod 755 "$next/RecruitmentSaaS"
python3 - <<'PY'
import os,pathlib
path=pathlib.Path('/etc/recruitmentsaas/app.env')
text=path.read_text()
keys=['StagingValidation__Enabled','AiSales__Enabled','AiSalesEngine__DraftsEnabled','AiSalesEngine__ExternalProcessingApproved','AiSalesDispatch__Enabled','AiSalesDispatch__ReleaseApproved','AiVoice__Enabled','AiVoice__ExternalProcessingApproved','AiChatLearning__Enabled','AiOfficeBooking__Enabled']
temp=path.with_name('app.env.installation-tmp')
temp.write_text(text+'\n# Explicitly disabled new AI features after owner-approved installation.\n'+'\n'.join(key+'=false' for key in keys)+'\n')
os.chmod(temp,0o600)
os.replace(temp,path)
PY
mv "$app" "$previous"
app_moved=1
mv "$next" "$app"
systemctl start recruitmentsaas
healthy=0
for attempt in $(seq 1 30); do
  if curl --noproxy '*' -fsS --max-time 3 http://127.0.0.1:8082/health > "$root/health.after"; then healthy=1; break; fi
  sleep 1
done
[[ $healthy -eq 1 ]] || exit 86
systemctl is-active --quiet recruitmentsaas
installed=1
printf '{"installationSucceeded":true,"originalDataVerified":true,"customerDispatchEnabled":false,"externalAIProcessingEnabled":false,"agentActivated":false}\n' > "$root/result.json"
printf 'Installation completed; new AI features remain disabled.\n'
