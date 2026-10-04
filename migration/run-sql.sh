#!/usr/bin/env bash
# Runs SQL files on the OVH server's PostgreSQL as the app owner role, in one transaction, stopping at the first error.
# Usage: migration/run-sql.sh <database> <file.sql>...
set -euo pipefail
DB="$1"; shift
{ echo "SET ROLE recruitapp;"; cat "$@"; } | ssh -o BatchMode=yes ubuntu@158.69.206.138 "sudo -n -u postgres psql -X -q -v ON_ERROR_STOP=1 --single-transaction -d '$DB'"
