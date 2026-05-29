#!/usr/bin/env bash
# Clean out-of-session rows from quote_snapshots (HQQQ).
#
# Purpose:
#   Delete quote_snapshots rows that fall OUTSIDE the regular trading
#   session (09:30-16:00 America/New_York, Mon-Fri) for basket_id='HQQQ'.
#   This matches the persistence write-gate and the gateway /api/history
#   read filter, so after a one-time cleanup the historical table contains
#   regular-session data only.
#
# Safety model:
#   1. ALWAYS prints a SELECT COUNT(*) preview (plus oldest/newest match)
#      before doing anything destructive.
#   2. Requires the explicit --confirm flag to actually DELETE. Without it
#      the script previews and exits 0.
#   3. Probes that the server has IANA tzdata ('America/New_York' resolves)
#      and REFUSES to delete if it does not.
#   4. Never touches Redis. Never touches raw_ticks. Never deletes rows
#      INSIDE the regular session.
#
# Usage:
#   ./scripts/clean-quote-snapshots-outside-rth.sh             # preview only
#   ./scripts/clean-quote-snapshots-outside-rth.sh --confirm   # preview + delete
#   ./scripts/clean-quote-snapshots-outside-rth.sh --basket HQQQ
#
# Prerequisites:
#   - psql on PATH.
#   - Timescale__ConnectionString set (Npgsql format, e.g.
#     "Host=...;Port=5432;Database=hqqq;Username=...;Password=...").
#
# Exit codes:
#   0 — preview completed (and delete completed if --confirm)
#   1 — misconfiguration (missing psql / connection string / tzdata)

set -euo pipefail

CONFIRM=0
BASKET_ID="HQQQ"
while [[ $# -gt 0 ]]; do
    case "$1" in
        --confirm) CONFIRM=1; shift ;;
        --basket) BASKET_ID="$2"; shift 2 ;;
        *) echo "Unknown argument: $1" >&2; exit 1 ;;
    esac
done

# Predicate identifying a regular-session point. Identical to the gateway
# TimescaleHistoryQueryService.SelectHistorySql RTH filter.
IN_SESSION_PREDICATE="(ts AT TIME ZONE 'America/New_York')::time >= TIME '09:30'
AND (ts AT TIME ZONE 'America/New_York')::time <  TIME '16:00'
AND EXTRACT(ISODOW FROM (ts AT TIME ZONE 'America/New_York')) BETWEEN 1 AND 5"

# ── Map Npgsql key=value;... to a libpq conninfo string ──
npgsql_to_libpq() {
    local conn="$1"
    local result=""
    local IFS=';'
    for segment in $conn; do
        [[ -z "$segment" ]] && continue
        local key="${segment%%=*}"
        local val="${segment#*=}"
        key="$(echo "$key" | tr '[:upper:]' '[:lower:]' | xargs)"
        val="$(echo "$val" | xargs)"
        local libpq_key=""
        case "$key" in
            host|server) libpq_key="host" ;;
            port) libpq_key="port" ;;
            database|db) libpq_key="dbname" ;;
            username|"user id"|userid|uid) libpq_key="user" ;;
            password|pwd) libpq_key="password" ;;
            "ssl mode"|sslmode) libpq_key="sslmode" ;;
            *) continue ;;
        esac
        # Quote values containing spaces.
        if [[ "$val" =~ [[:space:]] ]]; then
            val="'${val}'"
        fi
        result="${result}${libpq_key}=${val} "
    done
    echo "$result" | xargs
}

# ── Preconditions ───────────────────────────────────────
if ! command -v psql >/dev/null 2>&1; then
    echo "ERROR: psql not found on PATH. Install the PostgreSQL client." >&2
    exit 1
fi

if [[ -z "${Timescale__ConnectionString:-}" ]]; then
    echo "ERROR: Timescale__ConnectionString is not set in the environment." >&2
    exit 1
fi

CONNINFO="$(npgsql_to_libpq "${Timescale__ConnectionString}")"
if [[ -z "$CONNINFO" ]]; then
    echo "ERROR: Could not derive a libpq connection string from Timescale__ConnectionString." >&2
    exit 1
fi

psql_q() {
    psql "$CONNINFO" -t -A -v ON_ERROR_STOP=1 -c "$1"
}

# ── tzdata sanity probe ─────────────────────────────────
echo "==> Verifying server timezone database ('America/New_York')..."
TZ_OK="$(psql_q "SELECT (now() AT TIME ZONE 'America/New_York') IS NOT NULL;" | xargs)"
if [[ "$TZ_OK" != "t" ]]; then
    echo "ERROR: Server cannot resolve 'America/New_York'. Refusing to delete." >&2
    exit 1
fi

# ── Preview ─────────────────────────────────────────────
BASKET_LITERAL="${BASKET_ID//\'/\'\'}"
OUT_SESSION_WHERE="basket_id = '${BASKET_LITERAL}' AND NOT (${IN_SESSION_PREDICATE})"

echo "==> Preview: rows OUTSIDE regular session for basket_id='${BASKET_ID}'"
PREVIEW="$(psql_q "SELECT COUNT(*), COALESCE(MIN(ts)::text,'(none)'), COALESCE(MAX(ts)::text,'(none)') FROM quote_snapshots WHERE ${OUT_SESSION_WHERE};")"
COUNT="$(echo "$PREVIEW" | cut -d'|' -f1 | xargs)"
OLDEST="$(echo "$PREVIEW" | cut -d'|' -f2)"
NEWEST="$(echo "$PREVIEW" | cut -d'|' -f3)"
echo "    out-of-session rows : ${COUNT}"
echo "    oldest match (UTC)  : ${OLDEST}"
echo "    newest match (UTC)  : ${NEWEST}"

if [[ "$COUNT" == "0" ]]; then
    echo "Nothing to delete. Done."
    exit 0
fi

if [[ "$CONFIRM" -ne 1 ]]; then
    echo ""
    echo "Preview only. Re-run with --confirm to DELETE the ${COUNT} out-of-session rows."
    exit 0
fi

# ── Delete ──────────────────────────────────────────────
echo "==> Deleting ${COUNT} out-of-session rows (single transaction)..."
DELETED="$(psql_q "BEGIN; WITH d AS (DELETE FROM quote_snapshots WHERE ${OUT_SESSION_WHERE} RETURNING 1) SELECT COUNT(*) FROM d; COMMIT;" | xargs)"
echo "Deleted ${DELETED} rows."
exit 0
