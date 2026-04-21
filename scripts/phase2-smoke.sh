#!/usr/bin/env bash
# Phase 2 local smoke helper.
#
# Purpose:
#   Validate that the self-sufficient Phase 2 stack is running end-to-end
#   without the legacy hqqq-api monolith. Direct assertions per service
#   (no "downstream looks ok therefore upstream worked" inference):
#     - reference-data published the active basket (with AdjustmentSummary)
#     - ingress /healthz/ready exposes basket-derived runtime data and
#       tick flow is actually advancing (PublishedTickCount grows OR
#       LastPublishedTickUtc becomes recent across the window)
#     - quote-engine computed a positive iNAV into Redis
#     - gateway serves /api/quote and /api/constituents
#
# Usage:
#   ./scripts/phase2-smoke.sh
#
# Environment overrides:
#   HQQQ_GATEWAY_BASE_URL          (default: http://localhost:5030)
#   HQQQ_REFDATA_BASE_URL          (default: http://localhost:5020)
#   HQQQ_INGRESS_BASE_URL          (default: http://localhost:5081)
#   HQQQ_SMOKE_WARMUP_SECONDS      (default: 180)
#   HQQQ_SMOKE_TICK_WINDOW_SECONDS (default: 30)
#
# Exit codes:
#   0  — no critical failures (warnings are allowed)
#   1  — a critical precondition failed (docker/compose unreachable,
#                                         ingress readiness missing
#                                         basket fields, no live tick
#                                         flow inside the tick-window,
#                                         or E2E warmup did not deliver
#                                         nav>0 + non-empty holdings +
#                                         AdjustmentSummary)

set -u

critical_failures=0
warnings=0

warmup_seconds="${HQQQ_SMOKE_WARMUP_SECONDS:-180}"
tick_window_seconds="${HQQQ_SMOKE_TICK_WINDOW_SECONDS:-30}"
gateway_base="${HQQQ_GATEWAY_BASE_URL:-http://localhost:5030}"
refdata_base="${HQQQ_REFDATA_BASE_URL:-http://localhost:5020}"
ingress_base="${HQQQ_INGRESS_BASE_URL:-http://localhost:5081}"

color_reset="\033[0m"; color_green="\033[32m"; color_yellow="\033[33m"
color_red="\033[31m"; color_cyan="\033[36m"; color_gray="\033[90m"

section() { printf "\n${color_cyan}=== %s ===${color_reset}\n" "$1"; }
pass()    { printf "  ${color_green}[PASS]${color_reset} %s\n" "$1"; }
warn()    { printf "  ${color_yellow}[WARN]${color_reset} %s\n" "$1"; warnings=$((warnings+1)); }
info()    { printf "  ${color_gray}[INFO]${color_reset} %s\n" "$1"; }
fail()    { printf "  ${color_red}[FAIL]${color_reset} %s\n" "$1"; critical_failures=$((critical_failures+1)); }

printf "\n${color_green}HQQQ Phase 2 smoke helper (self-sufficient runtime)${color_reset}\n"
printf "  gateway base         : %s\n" "$gateway_base"
printf "  reference-data base  : %s\n" "$refdata_base"
printf "  ingress base         : %s\n" "$ingress_base"
printf "  warmup window        : %ss\n" "$warmup_seconds"
printf "  ingress tick window  : %ss\n" "$tick_window_seconds"

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
# 2. Kafka topics
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
# 3. Gateway + reference-data health probes
# ------------------------------------------------------------------
section "3. Gateway + reference-data health probes"

probe_endpoint() {
    local url="$1"
    local label="$2"
    local pass_csv="$3"
    local warn_msg="${4:-}"

    local code
    code="$(curl -s -o /dev/null -w '%{http_code}' --max-time 5 "$url" 2>/dev/null || true)"

    if [ -z "$code" ] || [ "$code" = "000" ]; then
        warn "$label : unreachable"
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

probe_endpoint "$gateway_base/healthz/live"  "gateway /healthz/live"  "200"
probe_endpoint "$gateway_base/healthz/ready" "gateway /healthz/ready" "200"
probe_endpoint "$refdata_base/healthz/ready" "reference-data /healthz/ready" "200"

# ------------------------------------------------------------------
# 4. System-health rollup
# ------------------------------------------------------------------
section "4. Gateway /api/system/health rollup"

if ! command -v jq >/dev/null 2>&1; then
    warn "jq not installed; skipping system-health JSON checks"
else
    body="$(mktemp)"
    code="$(curl -s -o "$body" -w '%{http_code}' --max-time 5 "$gateway_base/api/system/health" 2>/dev/null || echo '000')"
    if [ "$code" = "200" ]; then
        status="$(jq -r '.status' "$body")"
        deps="$(jq -r '.dependencies | map(.name + "=" + .status) | join(", ")' "$body")"
        info "top-level status : $status"
        info "dependencies     : $deps"
        if [ "$status" = "unhealthy" ]; then
            fail "system/health reports unhealthy — Phase 2 required services missing"
        else
            pass "system/health reachable and not unhealthy"
        fi
    else
        fail "system/health not reachable (HTTP $code)"
    fi
    rm -f "$body"
fi

# ------------------------------------------------------------------
# 5. /api/basket/current — AdjustmentSummary presence
# ------------------------------------------------------------------
section "5. /api/basket/current — AdjustmentSummary presence"

if ! command -v jq >/dev/null 2>&1; then
    warn "jq not installed; skipping AdjustmentSummary check"
else
    deadline=$(( $(date +%s) + warmup_seconds ))
    basket_ok=0
    while [ "$(date +%s)" -lt "$deadline" ]; do
        body="$(mktemp)"
        code="$(curl -s -o "$body" -w '%{http_code}' --max-time 5 "$refdata_base/api/basket/current" 2>/dev/null || echo '000')"
        if [ "$code" = "200" ] && jq -e '.adjustmentSummary' "$body" >/dev/null 2>&1; then
            summary_src="$(jq -r '.adjustmentSummary.source' "$body")"
            splits="$(jq -r '.adjustmentSummary.splitsApplied' "$body")"
            renames="$(jq -r '.adjustmentSummary.renamesApplied' "$body")"
            pass "/api/basket/current : adjustmentSummary present (source=$summary_src, splits=$splits, renames=$renames)"
            basket_ok=1
            rm -f "$body"
            break
        fi
        rm -f "$body"
        sleep 3
    done
    if [ "$basket_ok" -ne 1 ]; then
        fail "/api/basket/current : adjustmentSummary did not appear within ${warmup_seconds}s"
    fi
fi

# ------------------------------------------------------------------
# 5b. Ingress /healthz/ready — direct proof of basket-driven subscription
# ------------------------------------------------------------------
section "5b. Ingress /healthz/ready — basket-derived state"

initial_published=""
ingress_ready_ok=0

if ! command -v jq >/dev/null 2>&1; then
    warn "jq not installed; skipping ingress readiness JSON checks"
else
    deadline=$(( $(date +%s) + warmup_seconds ))
    while [ "$(date +%s)" -lt "$deadline" ]; do
        body="$(mktemp)"
        code="$(curl -s -o "$body" -w '%{http_code}' --max-time 5 "$ingress_base/healthz/ready" 2>/dev/null || echo '000')"
        if { [ "$code" = "200" ] || [ "$code" = "503" ]; } && [ -s "$body" ]; then
            applied_count="$(jq -r '(.dependencies // []) | map(select(.name=="ingress-basket")) | .[0].data.appliedSymbolCount // 0' "$body" 2>/dev/null)"
            applied_fp="$(jq -r '(.dependencies // []) | map(select(.name=="ingress-basket")) | .[0].data.appliedFingerprint // ""' "$body" 2>/dev/null)"
            last_applied="$(jq -r '(.dependencies // []) | map(select(.name=="ingress-basket")) | .[0].data.lastAppliedUtc // ""' "$body" 2>/dev/null)"
            upstream_status="$(jq -r '(.dependencies // []) | map(select(.name=="tiingo-upstream")) | .[0].status // ""' "$body" 2>/dev/null)"
            upstream_connected="$(jq -r '(.dependencies // []) | map(select(.name=="tiingo-upstream")) | .[0].data.isUpstreamConnected // false' "$body" 2>/dev/null)"
            published_now="$(jq -r '(.dependencies // []) | map(select(.name=="tiingo-upstream")) | .[0].data.publishedTickCount // 0' "$body" 2>/dev/null)"

            if [ -n "$applied_fp" ] && [ "$applied_count" -gt 0 ] 2>/dev/null; then
                pass "ingress.basket.appliedFingerprint = $applied_fp"
                pass "ingress.basket.appliedSymbolCount = $applied_count"
                if [ -n "$last_applied" ]; then
                    pass "ingress.basket.lastAppliedUtc    = $last_applied"
                else
                    warn "ingress.basket.lastAppliedUtc not present"
                fi
                if [ -n "$upstream_status" ]; then
                    pass "ingress.upstream.status           = $upstream_status; isUpstreamConnected=$upstream_connected"
                    initial_published="$published_now"
                    ingress_ready_ok=1
                else
                    fail "ingress /healthz/ready does not contain a 'tiingo-upstream' dependency"
                fi
                rm -f "$body"
                break
            fi
        fi
        rm -f "$body"
        sleep 3
    done

    if [ "$ingress_ready_ok" -ne 1 ]; then
        fail "ingress /healthz/ready did not expose appliedFingerprint + appliedSymbolCount > 0 within ${warmup_seconds}s"
    fi
fi

# ------------------------------------------------------------------
# 5c. Ingress live tick flow — direct PublishedTickCount growth proof
# ------------------------------------------------------------------
section "5c. Ingress live tick flow"

if ! command -v jq >/dev/null 2>&1; then
    warn "jq not installed; skipping live-tick-flow check"
elif [ -z "$initial_published" ]; then
    warn "skipping live-tick-flow check (no ingress readiness baseline captured)"
else
    info "baseline publishedTickCount=$initial_published; sampling again in ${tick_window_seconds}s"
    sleep "$tick_window_seconds"

    body="$(mktemp)"
    code="$(curl -s -o "$body" -w '%{http_code}' --max-time 5 "$ingress_base/healthz/ready" 2>/dev/null || echo '000')"
    ticks_ok=0
    last_utc_ok=0

    if { [ "$code" = "200" ] || [ "$code" = "503" ]; } && [ -s "$body" ]; then
        after_published="$(jq -r '(.dependencies // []) | map(select(.name=="tiingo-upstream")) | .[0].data.publishedTickCount // 0' "$body" 2>/dev/null)"
        last_published_utc="$(jq -r '(.dependencies // []) | map(select(.name=="tiingo-upstream")) | .[0].data.lastPublishedTickUtc // ""' "$body" 2>/dev/null)"

        if [ "$after_published" -gt "$initial_published" ] 2>/dev/null; then
            delta=$(( after_published - initial_published ))
            pass "ingress.publishedTickCount grew by $delta in ${tick_window_seconds}s (baseline=$initial_published → $after_published)"
            ticks_ok=1
        fi

        if [ -n "$last_published_utc" ] && command -v date >/dev/null 2>&1; then
            # GNU date understands ISO-8601; macOS BSD date does not. Soft-fail.
            last_epoch="$(date -d "$last_published_utc" +%s 2>/dev/null || echo '')"
            if [ -n "$last_epoch" ]; then
                age=$(( $(date +%s) - last_epoch ))
                if [ "$age" -lt $(( tick_window_seconds * 2 )) ]; then
                    pass "ingress.lastPublishedTickUtc is recent (age=${age}s)"
                    last_utc_ok=1
                else
                    warn "ingress.lastPublishedTickUtc is stale (age=${age}s)"
                fi
            fi
        fi
    fi
    rm -f "$body"

    if [ "$ticks_ok" -ne 1 ] && [ "$last_utc_ok" -ne 1 ]; then
        fail "ingress did not publish any ticks within the ${tick_window_seconds}s tick-window — Tiingo upstream is not flowing"
    elif [ "$ticks_ok" -ne 1 ]; then
        warn "ingress.publishedTickCount did not advance, but lastPublishedTickUtc is recent (acceptable fallback)"
    fi
fi

# ------------------------------------------------------------------
# 6. End-to-end: nav>0 + non-empty holdings
# ------------------------------------------------------------------
section "6. End-to-end: nav>0 and non-empty holdings"

if ! command -v jq >/dev/null 2>&1; then
    fail "jq is not installed; cannot validate quote/constituents payload shape"
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

        if [ "$quote_ok" -eq 1 ] && [ "$cons_ok" -eq 1 ]; then break; fi
        sleep 5
    done

    if [ "$quote_ok" -ne 1 ]; then
        fail "/api/quote : did not produce nav>0 within ${warmup_seconds}s warmup window"
    fi
    if [ "$cons_ok" -ne 1 ]; then
        fail "/api/constituents : did not produce non-empty holdings within ${warmup_seconds}s warmup window"
    fi
fi

# ------------------------------------------------------------------
# 7. Gateway history (Timescale path; empty 1D payload is acceptable)
# ------------------------------------------------------------------
section "7. Gateway /api/history?range=1D"

probe_endpoint \
    "$gateway_base/api/history?range=1D" \
    "/api/history?range=1D" \
    "200" \
    "history_unavailable — Timescale not reachable; populate hqqq-persistence or start TimescaleDB"

info "HTTP 200 with empty payload (pointCount=0) is a clean state on a fresh environment."

# ------------------------------------------------------------------
# Summary
# ------------------------------------------------------------------
section "Summary"

printf "  warnings           : %d\n" "$warnings"
printf "  critical failures  : %d\n" "$critical_failures"

if [ "$critical_failures" -gt 0 ]; then exit 1; fi
exit 0
