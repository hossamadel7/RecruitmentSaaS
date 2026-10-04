#!/usr/bin/env bash
#
# Server-side privileged deploy step for RecruitmentSaaS.
# Installed at /usr/local/bin/recruitmentsaas-deploy.sh on the VPS and run as root via sudo
# (whitelisted with NOPASSWD for the deploy user). See deploy/README.md.
#
# Swaps the uploaded build into place and restarts the service. Extraction is an OVERLAY,
# so wwwroot/uploads (candidate documents, contracts, visas) is never touched.
#
set -euo pipefail

APP_DIR="/var/www/recruitmentsaas"
SERVICE="recruitmentsaas"
TARBALL="/home/ubuntu/recruitmentsaas-deploy.tgz"

[ -f "$TARBALL" ] || { echo "ERROR: no tarball at $TARBALL"; exit 1; }

mkdir -p "$APP_DIR/wwwroot/uploads" /var/lib/recruitmentsaas/keys

echo "-- stopping $SERVICE"
systemctl stop "$SERVICE" || true

echo "-- extracting new build into $APP_DIR (preserving uploads)"
tar xzf "$TARBALL" -C "$APP_DIR"
chmod +x "$APP_DIR/RecruitmentSaaS"

echo "-- setting ownership to www-data"
chown -R www-data:www-data "$APP_DIR" /var/lib/recruitmentsaas

echo "-- starting $SERVICE"
systemctl start "$SERVICE"

echo "-- cleanup"
rm -f "$TARBALL"

echo "-- done"
