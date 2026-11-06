"""Privileged, explicitly requested code/schema installation; no AI/customer activation.

Invoke with a new private installation directory containing a reviewed manifest,
configuration-free application archive, upgrade SQL and aggregate data checker.
"""
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tarfile
import time

root = Path(sys.argv[1])
if os.geteuid() != 0 or not re.fullmatch(r"/home/ubuntu/\.local/share/recruitmentsaas-installations/[a-zA-Z0-9-]+", str(root)):
    raise SystemExit("Invalid installation identity")
manifest = json.loads((root / "manifest.json").read_text())
app = Path("/var/www/recruitmentsaas")
next_app = Path("/var/www/recruitmentsaas-releases") / (root.name + "-new")
previous = next_app.with_name(root.name + "-previous")
env_file = Path("/etc/recruitmentsaas/app.env")
if not app.is_dir() or app.is_symlink() or next_app.exists() or previous.exists():
    raise SystemExit("Unsafe release paths")

def digest(path):
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for part in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(part)
    return h.hexdigest()

for file in ("application-no-config.tar.gz", "upgrade.sql", "deployment_data_check.py"):
    if digest(root / file) != manifest["hashes"][file]:
        raise SystemExit("Release input checksum mismatch")

def run(args, **kwargs):
    return subprocess.run(args, check=True, **kwargs)

psql = ["runuser", "-u", "postgres", "--", "/usr/lib/postgresql/16/bin/psql", "-X", "-At", "-v", "ON_ERROR_STOP=1", "-d", "RecruitmentCRM"]
history = run(psql, input='SELECT max("MigrationId") FROM public."__EFMigrationsHistory";', text=True, capture_output=True).stdout.strip()
if history != manifest["baseline"]:
    raise SystemExit("Production baseline changed; reconcile before installation")
with tarfile.open(root / "application-no-config.tar.gz") as archive:
    for member in archive.getmembers():
        path = Path(member.name)
        if not member.isfile() or path.is_absolute() or ".." in path.parts or path.name.startswith("appsettings") or "uploads" in path.parts or any(p.startswith(".") for p in path.parts):
            raise SystemExit("Unexpected archive entry")

def immutable_files():
    paths = list(app.glob("appsettings*.json"))
    paths += list((app / "wwwroot/uploads").rglob("*")) if (app / "wwwroot/uploads").exists() else []
    paths += list(Path("/var/lib/recruitmentsaas/keys").rglob("*"))
    return {str(p): digest(p) for p in paths if p.is_file()}

def disable_outbound():
    keys = ["AiSalesEngine__AutoReplyEnabled", "AiSalesDispatch__Enabled", "AiSalesDispatch__AutomaticEnabled", "AiSalesDispatch__ReleaseApproved",
            "AiSalesLearning__Enabled", "AiSalesLearning__BackgroundEnabled", "AiSalesLearning__ExternalProcessingApproved", "AiSalesLearning__PrivacyApproved",
            "AiSalesLearning__SemanticRetrievalEnabled", "AiAdminLearning__Enabled", "AiAdminLearning__PrivateRoutingEnabled",
            "AiAdminLearning__BackgroundEnabled", "AiAdminLearning__OutboundApproved", "StagingValidation__Enabled"]
    text = env_file.read_text() + "\n# Reviewed SalesAgent deployment: customer auto-send and new learning remain disabled.\n"
    text += "\n".join(k + "=false" for k in keys) + "\n"
    temp = env_file.with_name("app.env.sales-release-tmp")
    temp.write_text(text)
    os.chmod(temp, 0o600)
    os.replace(temp, env_file)

run(["systemctl", "is-active", "--quiet", "recruitmentsaas"])
shutil.copy2(env_file, root / "app.env.before")
os.chmod(root / "app.env.before", 0o600)
(root / "service.before").write_bytes(run(["systemctl", "cat", "recruitmentsaas"], capture_output=True).stdout)
shutil.copytree("/var/lib/recruitmentsaas/keys", root / "keys.before")
next_app.parent.mkdir(exist_ok=True)
stopped = moved = installed = False
try:
    run(["systemctl", "stop", "recruitmentsaas"])
    stopped = True
    preserved = immutable_files()
    (root / "files.before.json").write_text(json.dumps(preserved))
    with (root / "RecruitmentCRM-before.dump").open("wb") as backup:
        run(["runuser", "-u", "postgres", "--", "/usr/lib/postgresql/16/bin/pg_dump", "--format=custom", "--no-owner", "--no-acl", "--lock-wait-timeout=10s", "RecruitmentCRM"], stdout=backup)
    (root / "database-backup.sha256").write_text(digest(root / "RecruitmentCRM-before.dump") + "\n")
    run(["python3", str(root / "deployment_data_check.py"), "before", str(root)])
    migration_env = dict(os.environ, PGOPTIONS="-c lock_timeout=10s -c statement_timeout=120s")
    with (root / "upgrade.sql").open("rb") as sql, (root / "migration.log").open("wb") as log:
        run(psql, stdin=sql, stdout=log, stderr=subprocess.STDOUT, env=migration_env)
    run(["python3", str(root / "deployment_data_check.py"), "after", str(root)])
    run(["cp", "-a", str(app), str(next_app)])
    with tarfile.open(root / "application-no-config.tar.gz") as archive:
        for member in archive.getmembers():
            destination = next_app / member.name
            if destination.is_symlink():
                raise RuntimeError("Refusing to overwrite application symlink")
            destination.parent.mkdir(parents=True, exist_ok=True)
            with archive.extractfile(member) as source, destination.open("wb") as target:
                shutil.copyfileobj(source, target)
            os.chmod(destination, 0o755 if member.name == "RecruitmentSaaS" else 0o644)
            shutil.chown(destination, user="www-data", group="www-data")
    shutil.chown(next_app, user="www-data", group="www-data")
    if digest(next_app / "RecruitmentSaaS.dll") != manifest["dllSha256"]:
        raise RuntimeError("Installed binary checksum mismatch")
    disable_outbound()
    app.rename(previous)
    moved = True
    next_app.rename(app)
    if immutable_files() != preserved:
        raise RuntimeError("Preserved settings/uploads/keys changed")
    run(["systemctl", "start", "recruitmentsaas"])
    healthy = False
    for _ in range(30):
        result = subprocess.run(["curl", "--noproxy", "*", "-fsS", "--max-time", "3", "http://127.0.0.1:8082/health"], capture_output=True)
        if result.returncode == 0:
            (root / "health.after").write_bytes(result.stdout)
            healthy = True
            break
        time.sleep(1)
    if not healthy:
        raise RuntimeError("Post-installation health check failed")
    run(["systemctl", "is-active", "--quiet", "recruitmentsaas"])
    installed = True
    result = dict(installed=True, commit=manifest["commit"], dllSha256=manifest["dllSha256"],
                  migration=manifest["latest"], originalDataPreserved=True, filesPreserved=True,
                  customerAutoRepliesEnabled=False, newLearningEnabled=False, previousRelease=str(previous))
    (root / "result.json").write_text(json.dumps(result, indent=2))
    print(json.dumps(result))
finally:
    if stopped and not installed:
        subprocess.run(["systemctl", "stop", "recruitmentsaas"])
        if moved:
            if app.exists():
                app.rename(root / "failed-application")
            previous.rename(app)
        shutil.copy2(root / "app.env.before", env_file)
        disable_outbound()
        subprocess.run(["systemctl", "start", "recruitmentsaas"])
        (root / "result.json").write_text(json.dumps(dict(installed=False, codeRollbackAttempted=True, databaseDownMigrationPerformed=False)))
