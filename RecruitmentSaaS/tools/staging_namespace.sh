#!/usr/bin/env bash
set -euo pipefail
stage=${1:?private staging directory required}
[[ "$stage" =~ ^/home/ubuntu/\.local/share/recruitmentsaas-staging/[a-zA-Z0-9-]+$ ]] || exit 80
[[ "$(readlink /proc/self/ns/net)" != "$(readlink /proc/1/ns/net)" ]] || exit 81
ip link set lo up
printf '%s\n' "$$" > "$stage/namespace.pid"
exec runuser -u ubuntu -- bash "$stage/staging_start.sh" "$stage" "$(readlink /proc/1/ns/net)"
