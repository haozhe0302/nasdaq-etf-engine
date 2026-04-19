#!/usr/bin/env bash
# Phase 2 — bring up the full local stack (infra + app tier).
#
# Thin wrapper around `docker compose` with the two-file overlay. Runs from
# the repo root regardless of where the user invokes it from. Excludes the
# `analytics` profile (one-shot job; see docs/phase2/local-dev.md).
#
# Usage:
#   ./scripts/phase2-up.sh             # build + up -d
#   ./scripts/phase2-up.sh --no-build  # up -d only (use existing images)

set -euo pipefail

NO_BUILD=0
for arg in "$@"; do
    case "$arg" in
        --no-build) NO_BUILD=1 ;;
        *) echo "Unknown argument: $arg" >&2; exit 2 ;;
    esac
done

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

cmd=(docker compose -f docker-compose.yml -f docker-compose.phase2.yml up -d)
if [[ "$NO_BUILD" -eq 0 ]]; then
    cmd+=(--build)
fi

printf '==> %s\n' "${cmd[*]}"
"${cmd[@]}"

cat <<'EOF'

Stack is starting. Next steps:
  1. Wait for kafka health (docker compose ps)
  2. Bootstrap topics:  ./scripts/bootstrap-kafka-topics.sh
  3. Smoke check:       ./scripts/phase2-smoke.sh

Endpoints:
  gateway          : http://localhost:5030
  reference-data   : http://localhost:5020/healthz/ready
  ingress mgmt     : http://localhost:5081/healthz/ready
  quote-engine mgmt: http://localhost:5082/healthz/ready
  persistence mgmt : http://localhost:5083/healthz/ready
EOF
