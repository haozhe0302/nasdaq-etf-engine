#!/usr/bin/env bash
# Phase 2D5 — bring up the replica-smoke stack (infra + app tier + 2nd gateway).
#
# Layered three-file compose: infra base + Phase 2 app overlay +
# replica-smoke overlay (adds hqqq-gateway-b on host port 5031).
# Excludes the `analytics` profile (one-shot job; see docs/phase2/local-dev.md).
#
# Usage:
#   ./scripts/replica-smoke-up.sh             # build + up -d
#   ./scripts/replica-smoke-up.sh --no-build  # up -d only (use existing images)

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

cmd=(docker compose
    -f docker-compose.yml
    -f docker-compose.phase2.yml
    -f docker-compose.replica-smoke.yml
    up -d)
if [[ "$NO_BUILD" -eq 0 ]]; then
    cmd+=(--build)
fi

printf '==> %s\n' "${cmd[*]}"
"${cmd[@]}"

cat <<'EOF'

Replica-smoke stack is starting. Next steps:
  1. Wait for kafka health (docker compose ps)
  2. Bootstrap topics:  ./scripts/bootstrap-kafka-topics.sh
  3. Run replica smoke: ./scripts/replica-smoke.sh

Endpoints:
  gateway-a (hqqq-gateway)   : http://localhost:5030
  gateway-b (hqqq-gateway-b) : http://localhost:5031
  reference-data             : http://localhost:5020/healthz/ready
  ingress mgmt               : http://localhost:5081/healthz/ready
  quote-engine mgmt          : http://localhost:5082/healthz/ready
  persistence mgmt           : http://localhost:5083/healthz/ready
EOF
