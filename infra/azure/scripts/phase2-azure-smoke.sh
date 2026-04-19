#!/usr/bin/env bash
# Phase 2 Azure post-deploy smoke probe.
#
# Exercises the gateway's externally-reachable surface against a real
# Azure Container Apps deployment. Also re-runnable locally by any
# operator with curl + jq.
#
# Required env:
#   GATEWAY_FQDN      - e.g. ca-hqqq-p2-gateway-demo-01.<env>.azurecontainerapps.io
#
# Optional env:
#   EXPECT_AGGREGATED - "true" (default) requires /api/system/health
#                       payload .sourceMode == "aggregated", proving the
#                       gateway is on the native aggregator (not legacy/stub).
#
# Exit codes:
#   0 - all probes passed
#   1 - at least one probe failed; failing endpoint(s) printed above

set -uo pipefail

GATEWAY_FQDN="${GATEWAY_FQDN:?GATEWAY_FQDN env var is required}"
EXPECT_AGGREGATED="${EXPECT_AGGREGATED:-true}"

base="https://${GATEWAY_FQDN}"

color_reset="\033[0m"
color_green="\033[32m"
color_red="\033[31m"
color_cyan="\033[36m"
color_gray="\033[90m"

passes=0
fails=0

section() { printf "\n${color_cyan}=== %s ===${color_reset}\n" "$1"; }
log_pass() { printf "  ${color_green}[PASS]${color_reset} %s\n" "$1"; passes=$((passes+1)); }
log_fail() { printf "  ${color_red}[FAIL]${color_reset} %s\n" "$1"; fails=$((fails+1)); }
log_info() { printf "  ${color_gray}[INFO]${color_reset} %s\n" "$1"; }

emit_body_excerpt() {
    local body_file="$1"
    if [[ -s "$body_file" ]]; then
        printf "${color_gray}    --- response body (first 1KB) ---${color_reset}\n"
        head -c 1024 "$body_file" | sed 's/^/    /'
        printf "\n${color_gray}    --- end ---${color_reset}\n"
    fi
}

# probe_http <label> <url> <body_out>
# Echoes HTTP status code; 000 = unreachable.
probe_http() {
    local label="$1"
    local url="$2"
    local body_out="$3"
    local code
    code=$(curl -sS -o "$body_out" -w '%{http_code}' --max-time 20 "$url" 2>/dev/null || echo "000")
    echo "$code"
}

require_jq() {
    if ! command -v jq >/dev/null 2>&1; then
        log_fail "jq is not installed; cannot validate JSON payload shape."
        exit 1
    fi
}

require_jq

printf "\n${color_green}HQQQ Phase 2 Azure smoke${color_reset}\n"
printf "  gateway base       : %s\n" "$base"
printf "  expect aggregated  : %s\n" "$EXPECT_AGGREGATED"

# ──────────────────────────────────────────────────────────────────
# 1. /healthz/live + /healthz/ready
# ──────────────────────────────────────────────────────────────────
section "1. Gateway liveness + readiness"

for ep in healthz/live healthz/ready; do
    body=$(mktemp)
    code=$(probe_http "/$ep" "$base/$ep" "$body")
    if [[ "$code" == "200" ]]; then
        log_pass "/$ep : HTTP 200"
    else
        log_fail "/$ep : HTTP $code (expected 200)"
        emit_body_excerpt "$body"
    fi
    rm -f "$body"
done

# ──────────────────────────────────────────────────────────────────
# 2. /api/system/health  (must be the gateway-native aggregator)
# ──────────────────────────────────────────────────────────────────
section "2. /api/system/health (aggregator)"

body=$(mktemp)
code=$(probe_http "/api/system/health" "$base/api/system/health" "$body")
if [[ "$code" != "200" ]]; then
    log_fail "/api/system/health : HTTP $code (expected 200)"
    emit_body_excerpt "$body"
else
    if ! jq -e . "$body" >/dev/null 2>&1; then
        log_fail "/api/system/health : HTTP 200 but body is not valid JSON"
        emit_body_excerpt "$body"
    else
        log_pass "/api/system/health : HTTP 200, valid JSON"
        if [[ "$EXPECT_AGGREGATED" == "true" ]]; then
            mode=$(jq -r '.sourceMode // ""' "$body")
            if [[ "$mode" == "aggregated" ]]; then
                log_pass "/api/system/health : sourceMode=\"aggregated\" (gateway-native)"
            else
                log_fail "/api/system/health : sourceMode=\"$mode\" — expected \"aggregated\". The gateway is on a legacy/stub adapter, not the native aggregator."
                emit_body_excerpt "$body"
            fi
        else
            log_info "EXPECT_AGGREGATED=false — skipping sourceMode assertion."
        fi
        deps=$(jq -r '.dependencies | length // 0' "$body")
        log_info "/api/system/health : reports $deps dependency entries."
    fi
fi
rm -f "$body"

# ──────────────────────────────────────────────────────────────────
# 3. /api/quote  (200 OR documented 503 quote_unavailable)
# ──────────────────────────────────────────────────────────────────
section "3. /api/quote (render-safe)"

body=$(mktemp)
code=$(probe_http "/api/quote" "$base/api/quote" "$body")
case "$code" in
    200)
        if jq -e . "$body" >/dev/null 2>&1; then
            log_pass "/api/quote : HTTP 200, valid JSON"
        else
            log_fail "/api/quote : HTTP 200 but body is not valid JSON"
            emit_body_excerpt "$body"
        fi
        ;;
    503)
        err=$(jq -r '.error // ""' "$body" 2>/dev/null || echo "")
        if [[ "$err" == "quote_unavailable" ]]; then
            log_pass "/api/quote : HTTP 503 quote_unavailable (documented cold-start state)"
        else
            log_fail "/api/quote : HTTP 503 but body is not the documented quote_unavailable shape (.error=\"$err\")"
            emit_body_excerpt "$body"
        fi
        ;;
    *)
        log_fail "/api/quote : HTTP $code (expected 200 or documented 503 quote_unavailable)"
        emit_body_excerpt "$body"
        ;;
esac
rm -f "$body"

# ──────────────────────────────────────────────────────────────────
# 4. /api/constituents  (200 OR documented 503 constituents_unavailable)
# ──────────────────────────────────────────────────────────────────
section "4. /api/constituents (render-safe)"

body=$(mktemp)
code=$(probe_http "/api/constituents" "$base/api/constituents" "$body")
case "$code" in
    200)
        if jq -e . "$body" >/dev/null 2>&1; then
            log_pass "/api/constituents : HTTP 200, valid JSON"
        else
            log_fail "/api/constituents : HTTP 200 but body is not valid JSON"
            emit_body_excerpt "$body"
        fi
        ;;
    503)
        err=$(jq -r '.error // ""' "$body" 2>/dev/null || echo "")
        if [[ "$err" == "constituents_unavailable" ]]; then
            log_pass "/api/constituents : HTTP 503 constituents_unavailable (documented cold-start state)"
        else
            log_fail "/api/constituents : HTTP 503 but body is not the documented constituents_unavailable shape (.error=\"$err\")"
            emit_body_excerpt "$body"
        fi
        ;;
    *)
        log_fail "/api/constituents : HTTP $code (expected 200 or documented 503 constituents_unavailable)"
        emit_body_excerpt "$body"
        ;;
esac
rm -f "$body"

# ──────────────────────────────────────────────────────────────────
# 5. /api/history?range=1D  (200 + render-safe JSON shape)
# ──────────────────────────────────────────────────────────────────
section "5. /api/history?range=1D (render-safe shape)"

body=$(mktemp)
code=$(probe_http "/api/history?range=1D" "$base/api/history?range=1D" "$body")
if [[ "$code" != "200" ]]; then
    log_fail "/api/history?range=1D : HTTP $code (expected 200; empty data is fine but must be 200, never 503)"
    emit_body_excerpt "$body"
else
    if ! jq -e . "$body" >/dev/null 2>&1; then
        log_fail "/api/history?range=1D : HTTP 200 but body is not valid JSON"
        emit_body_excerpt "$body"
    else
        # Required render-safe fields:
        #   pointCount: number >= 0
        #   series: array (may be empty)
        #   distribution: 21-bucket array (UI fixed-width)
        point_count=$(jq -r '.pointCount // empty' "$body")
        series_kind=$(jq -r '.series | type // empty' "$body")
        dist_len=$(jq -r '.distribution | length // empty' "$body")

        shape_ok=1
        if [[ -z "$point_count" ]] || ! [[ "$point_count" =~ ^[0-9]+$ ]]; then
            log_fail "/api/history : .pointCount is missing or not a non-negative integer (saw \"$point_count\")"
            shape_ok=0
        fi
        if [[ "$series_kind" != "array" ]]; then
            log_fail "/api/history : .series is missing or not an array (saw \"$series_kind\")"
            shape_ok=0
        fi
        if [[ -z "$dist_len" ]] || ! [[ "$dist_len" =~ ^[0-9]+$ ]] || [[ "$dist_len" -ne 21 ]]; then
            log_fail "/api/history : .distribution is missing or not a 21-bucket array (length=\"$dist_len\")"
            shape_ok=0
        fi
        if [[ "$shape_ok" -eq 1 ]]; then
            log_pass "/api/history?range=1D : HTTP 200, render-safe shape (pointCount=$point_count, series=array, distribution=21)"
        else
            emit_body_excerpt "$body"
        fi
    fi
fi
rm -f "$body"

# ──────────────────────────────────────────────────────────────────
# Summary
# ──────────────────────────────────────────────────────────────────
section "Summary"
printf "  passes : %d\n" "$passes"
printf "  fails  : %d\n" "$fails"

if [[ "$fails" -gt 0 ]]; then
    exit 1
fi
exit 0
