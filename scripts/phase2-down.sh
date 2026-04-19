#!/usr/bin/env bash
# Phase 2 — tear down the local stack (infra + app tier).
#
# Default behavior preserves named volumes (Timescale, Redis, quote-engine
# checkpoint, Prometheus/Grafana state). Pass --remove-volumes to drop them.
#
# Usage:
#   ./scripts/phase2-down.sh                 # stop + remove containers/networks
#   ./scripts/phase2-down.sh --remove-volumes  # also drop named volumes

set -euo pipefail

REMOVE_VOLUMES=0
for arg in "$@"; do
    case "$arg" in
        --remove-volumes|-v) REMOVE_VOLUMES=1 ;;
        *) echo "Unknown argument: $arg" >&2; exit 2 ;;
    esac
done

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

cmd=(docker compose -f docker-compose.yml -f docker-compose.phase2.yml down)
if [[ "$REMOVE_VOLUMES" -eq 1 ]]; then
    cmd+=(-v)
    echo "WARN: removing named volumes — Timescale, Redis, and quote-engine checkpoint will be lost." >&2
fi

printf '==> %s\n' "${cmd[*]}"
"${cmd[@]}"
