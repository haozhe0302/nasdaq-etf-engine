# Phase 2 local smoke helper.
#
# Purpose:
#   Validate the CURRENT Phase 2 local-dev environment and print actionable
#   results. This script does NOT start services, seed data, or assume
#   Timescale has been populated. Empty history / reports are reported as
#   a clean, explicit result rather than a hard failure.
#
# Usage:
#   .\scripts\phase2-smoke.ps1
#
# Environment overrides:
#   HQQQ_GATEWAY_BASE_URL (default: http://localhost:5030)
#
# Exit codes:
#   0  — no critical failures (warnings are allowed)
#   1  — a critical precondition failed (e.g. docker/compose not reachable)

$ErrorActionPreference = "Continue"

$script:CriticalFailures = 0
$script:Warnings = 0

function Write-Section {
    param([string]$Title)
    Write-Host ""
    Write-Host "=== $Title ===" -ForegroundColor Cyan
}

function Write-Pass {
    param([string]$Message)
    Write-Host "  [PASS] $Message" -ForegroundColor Green
}

function Write-Warn {
    param([string]$Message)
    Write-Host "  [WARN] $Message" -ForegroundColor Yellow
    $script:Warnings++
}

function Write-Info {
    param([string]$Message)
    Write-Host "  [INFO] $Message" -ForegroundColor Gray
}

function Write-Fail {
    param([string]$Message, [switch]$Critical)
    Write-Host "  [FAIL] $Message" -ForegroundColor Red
    if ($Critical) { $script:CriticalFailures++ }
}

$gatewayBase = $env:HQQQ_GATEWAY_BASE_URL
if ([string]::IsNullOrWhiteSpace($gatewayBase)) {
    $gatewayBase = "http://localhost:5030"
}

Write-Host ""
Write-Host "HQQQ Phase 2 smoke helper" -ForegroundColor Green
Write-Host "  gateway base : $gatewayBase"

# ------------------------------------------------------------------
# 1. Docker compose infra reachable
# ------------------------------------------------------------------
Write-Section "1. Docker compose infra"

$dockerOk = $false
try {
    docker --version 1>$null 2>$null
    if ($LASTEXITCODE -eq 0) { $dockerOk = $true }
} catch { }

if (-not $dockerOk) {
    Write-Fail "docker CLI not available on PATH" -Critical
} else {
    $expectedContainers = @("db", "cache", "kafka")
    foreach ($svc in $expectedContainers) {
        $status = (docker compose ps --format "{{.Service}}|{{.State}}|{{.Health}}" 2>$null `
            | Where-Object { $_ -like "$svc|*" } `
            | Select-Object -First 1)
        if (-not $status) {
            Write-Warn "$svc : container not found (run 'docker compose up -d')"
            continue
        }

        $parts  = $status -split '\|'
        $state  = if ($parts.Length -gt 1) { $parts[1] } else { "" }
        $health = if ($parts.Length -gt 2) { $parts[2] } else { "" }

        if ($state -eq "running" -and ($health -eq "healthy" -or [string]::IsNullOrEmpty($health))) {
            Write-Pass "$svc : running (health=$health)"
        } elseif ($state -eq "running") {
            Write-Warn "$svc : running but health=$health"
        } else {
            Write-Warn "$svc : state=$state health=$health"
        }
    }
}

# ------------------------------------------------------------------
# 2. Kafka topics present with expected partition counts
# ------------------------------------------------------------------
Write-Section "2. Kafka topics"

$expectedTopics = @(
    @{ Name = "market.raw_ticks.v1";          Partitions = 3 },
    @{ Name = "market.latest_by_symbol.v1";   Partitions = 3 },
    @{ Name = "refdata.basket.active.v1";     Partitions = 1 },
    @{ Name = "refdata.basket.events.v1";     Partitions = 1 },
    @{ Name = "pricing.snapshots.v1";         Partitions = 1 },
    @{ Name = "ops.incidents.v1";             Partitions = 1 }
)

$topicList = $null
if ($dockerOk) {
    $topicList = docker compose exec -T kafka /opt/kafka/bin/kafka-topics.sh `
        --bootstrap-server localhost:9092 --list 2>$null
}

if (-not $topicList) {
    Write-Warn "could not enumerate topics (is kafka healthy? run scripts/bootstrap-kafka-topics.ps1)"
} else {
    foreach ($t in $expectedTopics) {
        if ($topicList -notcontains $t.Name) {
            Write-Warn "$($t.Name) : missing — run scripts/bootstrap-kafka-topics.ps1"
            continue
        }

        $desc = docker compose exec -T kafka /opt/kafka/bin/kafka-topics.sh `
            --bootstrap-server localhost:9092 --describe --topic $t.Name 2>$null
        $partCount = ($desc | Select-String -SimpleMatch "Partition:").Matches.Count
        if ($partCount -eq $t.Partitions) {
            Write-Pass "$($t.Name) : partitions=$partCount"
        } elseif ($partCount -gt 0) {
            Write-Warn "$($t.Name) : partitions=$partCount, expected $($t.Partitions)"
        } else {
            Write-Warn "$($t.Name) : could not read partition count"
        }
    }
}

# ------------------------------------------------------------------
# 3. Gateway health probes
# ------------------------------------------------------------------
Write-Section "3. Gateway health probes"

function Test-Endpoint {
    param(
        [string]$Url,
        [string]$Label,
        [int[]]$PassCodes = @(200),
        [hashtable]$WarnCodes = @{}
    )
    try {
        $resp = Invoke-WebRequest -Uri $Url -Method GET -TimeoutSec 5 -ErrorAction Stop
        $code = $resp.StatusCode
    } catch {
        if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
            $code = [int]$_.Exception.Response.StatusCode.value__
        } else {
            Write-Warn "$Label : gateway unreachable ($($_.Exception.Message.Trim()))"
            return
        }
    }

    if ($PassCodes -contains $code) {
        Write-Pass "$Label : HTTP $code"
    } elseif ($WarnCodes.ContainsKey($code)) {
        Write-Warn "$Label : HTTP $code — $($WarnCodes[$code])"
    } else {
        Write-Warn "$Label : HTTP $code"
    }
}

Test-Endpoint -Url "$gatewayBase/healthz/live"  -Label "/healthz/live"
Test-Endpoint -Url "$gatewayBase/healthz/ready" -Label "/healthz/ready"

# ------------------------------------------------------------------
# 4. Gateway history (C2 Timescale mode)
# ------------------------------------------------------------------
Write-Section "4. Gateway /api/history?range=1D (render-safe)"

Test-Endpoint `
    -Url "$gatewayBase/api/history?range=1D" `
    -Label "/api/history?range=1D" `
    -PassCodes @(200) `
    -WarnCodes @{
        503 = "history_unavailable — Timescale not reachable; populate hqqq-persistence or start TimescaleDB";
        400 = "unexpected bad request";
    }

Write-Info "HTTP 200 with empty payload (pointCount=0) is a clean state on a fresh environment."

# ------------------------------------------------------------------
# 5. Analytics sample
# ------------------------------------------------------------------
Write-Section "5. Analytics one-shot report"

$analyticsStart = $env:Analytics__StartUtc
$analyticsEnd   = $env:Analytics__EndUtc

$sampleCommand = @"
`$env:Timescale__ConnectionString = 'Host=localhost;Port=5432;Database=hqqq;Username=admin;Password=changeme'
`$env:Analytics__Mode      = 'report'
`$env:Analytics__BasketId  = 'HQQQ'
`$env:Analytics__StartUtc  = '2026-04-17T00:00:00Z'
`$env:Analytics__EndUtc    = '2026-04-18T00:00:00Z'
dotnet run --project src/services/hqqq-analytics/hqqq-analytics.csproj
"@

if ([string]::IsNullOrWhiteSpace($analyticsStart) -or [string]::IsNullOrWhiteSpace($analyticsEnd)) {
    Write-Info "Analytics__StartUtc / Analytics__EndUtc not set — skipping execution."
    Write-Info "Sample invocation:"
    Write-Host ""
    Write-Host $sampleCommand -ForegroundColor DarkGray
    Write-Host ""
} else {
    Write-Info "StartUtc=$analyticsStart EndUtc=$analyticsEnd — attempting dotnet run"
    Push-Location (Split-Path -Parent $PSScriptRoot)
    try {
        dotnet run --project src/services/hqqq-analytics/hqqq-analytics.csproj
        switch ($LASTEXITCODE) {
            0 { Write-Pass "analytics exited 0 (including empty-window)" }
            1 { Write-Warn "analytics exited 1 — job failure (reader / artifact write / connection)" }
            2 { Write-Warn "analytics exited 2 — unsupported Analytics__Mode" }
            default { Write-Warn "analytics exited $LASTEXITCODE" }
        }
    } finally {
        Pop-Location
    }
}

# ------------------------------------------------------------------
# Summary
# ------------------------------------------------------------------
Write-Section "Summary"

Write-Host "  warnings           : $($script:Warnings)"
Write-Host "  critical failures  : $($script:CriticalFailures)"

if ($script:CriticalFailures -gt 0) {
    exit 1
}

exit 0
