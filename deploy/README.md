# Deploy — RecruitmentSaaS → OVH VPS

The app runs on the same VPS as QuizPlatform (`158.69.206.138`), as its own systemd service, on its own
port and database, so the two never touch each other.

| | |
|---|---|
| Service | `recruitmentsaas` (systemd), user `www-data` |
| App folder | `/var/www/recruitmentsaas` (self-contained .NET 8 build) |
| Port | `127.0.0.1:8082`, behind nginx |
| Database | PostgreSQL 16, database `RecruitmentCRM`, role `recruitapp` |
| Secrets | `/etc/recruitmentsaas/app.env` (root-only) |
| Uploads | `/var/www/recruitmentsaas/wwwroot/uploads` (kept across deploys) |
| Login keys | `/var/lib/recruitmentsaas/keys` (logins survive restarts) |

## Deploy

```bash
bash deploy/deploy.sh
```

Builds locally, uploads one tarball over SSH, the server swaps it in and restarts the service.

## One-time server setup (already done)

```bash
# service, deploy script, passwordless sudo for that one script
scp deploy/recruitmentsaas.service deploy/recruitmentsaas-deploy.sh ubuntu@158.69.206.138:/home/ubuntu/
ssh ubuntu@158.69.206.138 '
  sudo install -o root -g root -m 0644 recruitmentsaas.service /etc/systemd/system/recruitmentsaas.service &&
  sudo install -o root -g root -m 0755 recruitmentsaas-deploy.sh /usr/local/bin/recruitmentsaas-deploy.sh &&
  echo "ubuntu ALL=(root) NOPASSWD: /usr/local/bin/recruitmentsaas-deploy.sh" | sudo tee /etc/sudoers.d/recruitmentsaas-deploy &&
  sudo chmod 440 /etc/sudoers.d/recruitmentsaas-deploy &&
  sudo systemctl daemon-reload && sudo systemctl enable recruitmentsaas'
```

`/etc/recruitmentsaas/app.env` (mode 600, owner root) holds:

```text
ConnectionStrings__DefaultConnection=Host=localhost;Database=RecruitmentCRM;Username=recruitapp;Password=...
```

plus any setting that was overridden in the Azure App Service configuration, written the same way
(`Section__Key=value`).

## Database

The SQL Server → PostgreSQL move is scripted in `migration/` — see `migration/README.md`.
