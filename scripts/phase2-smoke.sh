#!/usr/bin/env bash
# Phase 2 local smoke helper.
#
# Purpose:
#   Validate the CURRENT Phase 2 local-dev environment and print actionable
#   results. This script does NOT start services, seed data, or assume
#   Timescale has been populated. Empty history / reports are reported as
#   a clean, explicit result rather than a hard failure.
#
# Usage:
#   ./scripts/phase2-smoke.sh                            # hybrid (default)
#   ./scripts/phase2-smoke.sh --mode standalone          # extra warmup checks
#   MODE=standalone ./scripts/phase2-smoke.sh            # same via env
#
# Environment overrides:
#   HQQQ_GATEWAY_BASE_URL     (default: http://localhost:5030)
#   HQQQ_SMOKE_WARMUP_SECONDS (default: 180; standalone-mode warmup window)
#
# Exit codes:
#   0  — no critical failures (warnings are allowed)
#   1  — a critical precondition failed (e.g. docker/compose not reachable,
#                                         or standalone warmup expectations
#                                         not satisfied within the window)

set -u

critical_failures=0
warnings=0

mode="${MODE:-hybrid}"
while [ $# -gt 0 ]; do
    case "$1" in
        --mode) mode="${2:-hybrid}"; shift 2 ;;
        --mode=*) mode="${1#*=}"; shift ;;
        *) shift ;;
    esac
done
if [ "$mode" != "hybrid" ] && [ "$mode" != "standalone" ]; then
    printf "Invalid --mode '%s' (expected 'hybrid' or 'standalone')\n" "$mode" >&2
    exit 2
fi

warmup_seconds="${HQQQ_SMOKE_WARMUP_SECONDS:-180}"

color_reset="\033[0m"
color_green="\033[32m"
color_yellow="\033[33m"
color_red="\033[31m"
color_cyan="\033[36m"
color_gray="\033[90m"

section() { printf "\n${color_cyan}=== %s ===${color_reset}\n" "$1"; }
pass()    { printf "  ${color_green}[PASS]${color_reset} %s\n" "$1"; }
warn()    { printf "  ${color_yellow}[WARN]${color_reset} %s\n" "$1"; warnings=$((warnings+1)); }
info()    { printf "  ${color_gray}[INFO]${color_reset} %s\n" "$1"; }
fail()    { printf "  ${color_red}[FAIL]${color_reset} %s\n" "$1"; critical_failures=$((critical_failures+1)); }

gateway_base="${HQQQ_GATEWAY_BASE_URL:-http://localhost:5030}"

printf "\n${color_green}HQQQ Phase 2 smoke helper${color_reset}\n"
printf "  gateway base    : %s\n" "$gateway_base"
printf "  operating mode  : %s\n" "$mode"
if [ "$mode" = "standalone" ]; then
    printf "  warmup window   : %ss\n" "$warmup_seconds"
fi

# ------------------------------------------------------------------
# 1. Docker compose infra reachable
# ------------------------------------------------------------------
section "1. Docker compose infra"

docker_ok=0
if command -v docker >/dev/null 2>&1; then
    docker_ok=1
else
    fail "docker CLI not available on PATH"
fi

if [ "$docker_ok" -eq 1 ]; then
    ps_output="$(docker compose ps --format '{{.Service}}|{{.State}}|{{.Health}}' 2>/dev/null || true)"
    for svc in db cache kafka; do
        line="$(printf "%s\n" "$ps_output" | grep -E "^${svc}\|" | head -n1 || true)"
        if [ -z "$line" ]; then
            warn "$svc : container not found (run 'docker compose up -d')"
            continue
        fi

        state="$(printf "%s" "$line" | awk -F'|' '{print $2}')"
        health="$(printf "%s" "$line" | awk -F'|' '{print $3}')"

        if [ "$state" = "running" ] && { [ "$health" = "healthy" ] || [ -z "$health" ]; }; then
            pass "$svc : running (health=${health:-<none>})"
        elif [ "$state" = "running" ]; then
            warn "$svc : running but health=$health"
        else
            warn "$svc : state=$state health=$health"
        fi
    done
fi

# ------------------------------------------------------------------
# 2. Kafka topics present with expected partition counts
# ------------------------------------------------------------------
section "2. Kafka topics"

topic_list=""
if [ "$docker_ok" -eq 1 ]; then
    topic_list="$(docker compose exec -T kafka /opt/kafka/bin/kafka-topics.sh \
        --bootstrap-server localhost:9092 --list 2>/dev/null || true)"
fi

if [ -z "$topic_list" ]; then
    warn "could not enumerate topics (is kafka healthy? run scripts/bootstrap-kafka-topics.sh)"
else
    # space-separated list of "name:expected_partitions" pairs
    expected_topics=(
        "market.raw_ticks.v1:3"
        "market.latest_by_symbol.v1:3"
        "refdata.basket.active.v1:1"
        "refdata.basket.events.v1:1"
        "pricing.snapshots.v1:1"
        "ops.incidents.v1:1"
    )

    for spec in "${expected_topics[@]}"; do
        topic="${spec%%:*}"
        expected="${spec##*:}"

        if ! printf "%s\n" "$topic_list" | grep -Fxq "$topic"; then
            warn "$topic : missing — run scripts/bootstrap-kafka-topics.sh"
            continue
        fi

        desc="$(docker compose exec -T kafka /opt/kafka/bin/kafka-topics.sh \
            --bootstrap-server localhost:9092 --describe --topic "$topic" 2>/dev/null || true)"
        part_count="$(printf "%s\n" "$desc" | grep -c 'Partition:' || true)"

        if [ "$part_count" -eq "$expected" ]; then
            pass "$topic : partitions=$part_count"
        elif [ "$part_count" -gt 0 ]; then
            warn "$topic : partitions=$part_count, expected $expected"
        else
            warn "$topic : could not read partition count"
        fi
    done
fi

# ------------------------------------------------------------------
# 3. Gateway health probes
# ------------------------------------------------------------------
section "3. Gateway health probes"

probe_endpoint() {
    # probe_endpoint <url> <label> <pass_codes_csv> [warn_msg_for_503]
    local url="$1"
    local label="$2"
    local pass_csv="$3"
    local warn_msg="${4:-}"

    local code
    code="$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 "$url" 2>/dev/null || true)"

    if [ -z "$code" ] || [ "$code" = "000" ]; then
        warn "$label : gateway unreachable"
        return
    fi

    if printf ",%s," "$pass_csv" | grep -q ",$code,"; then
        pass "$label : HTTP $code"
    elif [ "$code" = "503" ] && [ -n "$warn_msg" ]; then
        warn "$label : HTTP 503 — $warn_msg"
    else
        warn "$label : HTTP $code"
    fi
}

probe_endpoint "$gateway_base/healthz/live"  "/healthz/live"  "200"
probe_endpoint "$gateway_base/healthz/ready" "/healthz/ready" "200"

# ------------------------------------------------------------------
# 4. Gateway history (C2 Timescale mode)
# ------------------------------------------------------------------
section "4. Gateway /api/history?range=1D (render-safe)"

probe_endpoint \
    "$gateway_base/api/history?range=1D" \
    "/api/history?range=1D" \
    "200" \
    "history_unavailable — Timescale not reachable; populate hqqq-persistence or start TimescaleDB"

info "HTTP 200 with empty payload (pointCount=0) is a clean state on a fresh environment."

# ------------------------------------------------------------------
# 5. Analytics sample
# ------------------------------------------------------------------
section "5. Analytics one-shot report"

analytics_start="${Analytics__StartUtc:-}"
analytics_end="${Analytics__EndUtc:-}"

read -r -d '' sample_cmd <<'EOF' || true
export Timescale__ConnectionString='Host=localhost;Port=5432;Database=hqqq;Username=admin;Password=changeme'
export Analytics__Mode='report'
export Analytics__BasketId='HQQQ'
export Analytics__StartUtc='2026-04-17T00:00:00Z'
export Analytics__EndUtc='2026-04-18T00:00:00Z'
dotnet run --project src/services/hqqq-analytics/hqqq-analytics.csproj
EOF

if [ -z "$analytics_start" ] || [ -z "$analytics_end" ]; then
    info "Analytics__StartUtc / Analytics__EndUtc not set — skipping execution."
    info "Sample invocation:"
    printf "\n%s\n\n" "$sample_cmd"
else
    info "StartUtc=$analytics_start EndUtc=$analytics_end — attempting dotnet run"
    repo_root="$(cd "$(dirname "$0")/.." && pwd)"
    (
        cd "$repo_root" || exit 1
        dotnet run --project src/services/hqqq-analytics/hqqq-analytics.csproj
    )
    rc=$?
    case "$rc" in
        0) pass "analytics exited 0 (including empty-window)" ;;
        1) warn "analytics exited 1 — job failure (reader / artifact write / connection)" ;;
        2) warn "analytics exited 2 — unsupported Analytics__Mode" ;;
        *) warn "analytics exited $rc" ;;
    esac
fi

# ------------------------------------------------------------------
# 6. Standalone-mode warmup assertions
# ------------------------------------------------------------------
if [ "$mode" = "standalone" ]; then
    section "6. Standalone warmup (nav>0 & non-empty holdings)"

    if ! command -v jq >/dev/null 2>&1; then
        fail "jq is not installed; cannot validate standalone payload shape"
    else
        deadline=$(( $(date +%s) + warmup_seconds ))
        quote_ok=0
        cons_ok=0

        while [ "$(date +%s)" -lt "$deadline" ]; do
            if [ "$quote_ok" -ne 1 ]; then
                body="$(mktemp)"
                code="$(curl -s -o "$body" -w '%{http_code}' --max-time 5 \
                    "$gateway_base/api/quote" 2>/dev/null || echo '000')"
                if [ "$code" = "200" ] && jq -e '.nav and (.nav > 0)' "$body" >/dev/null 2>&1; then
                    nav="$(jq -r '.nav' "$body")"
                    pass "/api/quote : nav=$nav"
                    quote_ok=1
                fi
                rm -f "$body"
            fi

            if [ "$cons_ok" -ne 1 ]; then
                body="$(mktemp)"
                code="$(curl -s -o "$body" -w '%{http_code}' --max-time 5 \
                    "$gateway_base/api/constituents" 2>/dev/null || echo '000')"
                if [ "$code" = "200" ] && jq -e '.holdings | type == "array" and length > 0' "$body" >/dev/null 2>&1; then
                    count="$(jq -r '.holdings | length' "$body")"
                    pass "/api/constituents : holdings.length=$count"
                    cons_ok=1
                fi
                rm -f "$body"
            fi

            if [ "$quote_ok" -eq 1 ] && [ "$cons_ok" -eq 1 ]; then
                break
            fi
            sleep 5
        done

        if [ "$quote_ok" -ne 1 ]; then
            fail "/api/quote : did not produce nav>0 within ${warmup_seconds}s warmup window"
        fi
        if [ "$cons_ok" -ne 1 ]; then
            fail "/api/constituents : did not produce non-empty holdings within ${warmup_seconds}s warmup window"
        fi
    fi
fi

# ------------------------------------------------------------------
# Summary
# ------------------------------------------------------------------
section "Summary"

printf "  warnings           : %d\n" "$warnings"
printf "  critical failures  : %d\n" "$critical_failures"

if [ "$critical_failures" -gt 0 ]; then
    exit 1
fi

exit 0
