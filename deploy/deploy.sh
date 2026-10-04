#!/usr/bin/env bash
#
# One-command deploy for RecruitmentSaaS -> OVH VPS (same server as QuizPlatform).
# Run from Git Bash on your Windows machine:
#
#   bash deploy/deploy.sh
#
# Builds a self-contained linux-x64 release (it carries its own .NET 8 runtime, so nothing on the
# server changes), uploads one tarball over SSH, and the server swaps it in and restarts the service.
# Uploaded files (wwwroot/uploads) are preserved. Requires the one-time setup in deploy/README.md.
#
set -euo pipefail

# ----------------------------- config -----------------------------
VPS="ubuntu@158.69.206.138"                                   # VPS ssh target
SERVICE="recruitmentsaas"                                     # systemd service name
REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"   # this repo (auto)
PUBLISH_DIR="/c/deploy/recruitmentsaas"                       # local publish output (outside repo)
REMOTE_TARBALL="/home/ubuntu/recruitmentsaas-deploy.tgz"      # staging path on the VPS
# ------------------------------------------------------------------

echo "==> [1/4] Publishing (Release, linux-x64, self-contained)..."
rm -rf "$PUBLISH_DIR"
dotnet publish "$REPO_DIR/RecruitmentSaaS/RecruitmentSaaS.csproj" -c Release -r linux-x64 --self-contained true \
  -o "$PUBLISH_DIR" -nologo -v q

# Never ship local uploads or dev settings over the server's copies.
rm -rf "$PUBLISH_DIR/wwwroot/uploads" "$PUBLISH_DIR/appsettings.Development.json"

echo "==> [2/4] Packing..."
TARBALL="$(mktemp -d)/recruitmentsaas-deploy.tgz"
tar czf "$TARBALL" -C "$PUBLISH_DIR" .

echo "==> [3/4] Uploading to $VPS ..."
scp -q "$TARBALL" "$VPS:$REMOTE_TARBALL"

echo "==> [4/4] Installing on the server and restarting $SERVICE ..."
ssh "$VPS" "sudo /usr/local/bin/recruitmentsaas-deploy.sh"

echo "==> Health check"
sleep 3
ssh "$VPS" "curl -fsS http://127.0.0.1:8082/health && echo"
echo "==> Deployed."
