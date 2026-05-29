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
#   2. Requires the explicit -Confirm switch to actually DELETE. Without it
#      the script previews and exits 0.
#   3. Probes that the server has IANA tzdata ('America/New_York' resolves)
#      and REFUSES to delete if it does not — otherwise the NOT(...) clause
#      could match every row.
#   4. Never touches Redis. Never touches raw_ticks. Never deletes rows
#      INSIDE the regular session.
#
# Usage:
#   .\scripts\clean-quote-snapshots-outside-rth.ps1            # preview only
#   .\scripts\clean-quote-snapshots-outside-rth.ps1 -Confirm   # preview + delete
#
# Prerequisites:
#   - psql on PATH.
#   - Timescale__ConnectionString set in the environment (Npgsql format,
#     e.g. "Host=...;Port=5432;Database=hqqq;Username=...;Password=...").
#
# Exit codes:
#   0 — preview completed (and delete completed if -Confirm)
#   1 — misconfiguration (missing psql / connection string / tzdata)

param(
    [switch]$Confirm,
    [string]$BasketId = "HQQQ"
)

$ErrorActionPreference = "Stop"

# Predicate identifying a regular-session point. Kept identical to the
# gateway TimescaleHistoryQueryService.SelectHistorySql RTH filter.
$InSessionPredicate = @"
(ts AT TIME ZONE 'America/New_York')::time >= TIME '09:30'
AND (ts AT TIME ZONE 'America/New_York')::time <  TIME '16:00'
AND EXTRACT(ISODOW FROM (ts AT TIME ZONE 'America/New_York')) BETWEEN 1 AND 5
"@

function Convert-NpgsqlToLibpq([string]$conn) {
    # Map Npgsql key=value;... to a libpq conninfo string.
    $map = @{
        "host" = "host"; "server" = "host";
        "port" = "port";
        "database" = "dbname"; "db" = "dbname";
        "username" = "user"; "user id" = "user"; "userid" = "user"; "uid" = "user";
        "password" = "password"; "pwd" = "password";
        "ssl mode" = "sslmode"; "sslmode" = "sslmode";
    }
    $parts = @()
    foreach ($segment in $conn.Split(";")) {
        if ([string]::IsNullOrWhiteSpace($segment)) { continue }
        $kv = $segment.Split("=", 2)
        if ($kv.Count -ne 2) { continue }
        $key = $kv[0].Trim().ToLowerInvariant()
        $val = $kv[1].Trim()
        if ($map.ContainsKey($key)) {
            $libpqKey = $map[$key]
            # Quote values that contain spaces or special chars.
            if ($val -match "[\s']") { $val = "'" + ($val -replace "'", "\'") + "'" }
            $parts += "$libpqKey=$val"
        }
    }
    return ($parts -join " ")
}

# ── Preconditions ───────────────────────────────────────
if (-not (Get-Command psql -ErrorAction SilentlyContinue)) {
    Write-Error "psql not found on PATH. Install the PostgreSQL client."
    exit 1
}

$rawConn = $env:Timescale__ConnectionString
if ([string]::IsNullOrWhiteSpace($rawConn)) {
    Write-Error "Timescale__ConnectionString is not set in the environment."
    exit 1
}

$conninfo = Convert-NpgsqlToLibpq $rawConn
if ([string]::IsNullOrWhiteSpace($conninfo)) {
    Write-Error "Could not derive a libpq connection string from Timescale__ConnectionString."
    exit 1
}

function Invoke-Psql([string]$sql) {
    $out = & psql $conninfo -t -A -v ON_ERROR_STOP=1 -c $sql 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "psql failed: $out"
    }
    return ($out | Out-String).Trim()
}

# ── tzdata sanity probe ─────────────────────────────────
Write-Host "==> Verifying server timezone database ('America/New_York')..." -ForegroundColor Cyan
$tzOk = Invoke-Psql "SELECT (now() AT TIME ZONE 'America/New_York') IS NOT NULL;"
if ($tzOk -ne "t") {
    Write-Error "Server cannot resolve 'America/New_York'. Refusing to delete (tzdata missing would make the NOT(...) clause unsafe)."
    exit 1
}

# ── Preview ─────────────────────────────────────────────
$basketLiteral = $BasketId.Replace("'", "''")
$outSessionWhere = "basket_id = '$basketLiteral' AND NOT ($InSessionPredicate)"

Write-Host "==> Preview: rows OUTSIDE regular session for basket_id='$BasketId'" -ForegroundColor Cyan
$preview = Invoke-Psql @"
SELECT COUNT(*),
       COALESCE(MIN(ts)::text, '(none)'),
       COALESCE(MAX(ts)::text, '(none)')
FROM quote_snapshots
WHERE $outSessionWhere;
"@
$cols = $preview.Split("|")
$count = $cols[0]
Write-Host "    out-of-session rows : $count"
Write-Host "    oldest match (UTC)  : $($cols[1])"
Write-Host "    newest match (UTC)  : $($cols[2])"

if ($count -eq "0") {
    Write-Host "Nothing to delete. Done." -ForegroundColor Green
    exit 0
}

if (-not $Confirm) {
    Write-Host ""
    Write-Host "Preview only. Re-run with -Confirm to DELETE the $count out-of-session rows." -ForegroundColor Yellow
    exit 0
}

# ── Delete ──────────────────────────────────────────────
Write-Host "==> Deleting $count out-of-session rows (single transaction)..." -ForegroundColor Cyan
$deleted = Invoke-Psql @"
BEGIN;
WITH d AS (
  DELETE FROM quote_snapshots
  WHERE $outSessionWhere
  RETURNING 1
)
SELECT COUNT(*) FROM d;
COMMIT;
"@
Write-Host "Deleted $deleted rows." -ForegroundColor Green
exit 0
