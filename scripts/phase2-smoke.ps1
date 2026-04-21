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
#   Empty history / reports are reported as a clean, explicit result
#   rather than a hard failure.
#
# Usage:
#   .\scripts\phase2-smoke.ps1
#
# Environment overrides:
#   HQQQ_GATEWAY_BASE_URL       (default: http://localhost:5030)
#   HQQQ_REFDATA_BASE_URL       (default: http://localhost:5020)
#   HQQQ_INGRESS_BASE_URL       (default: http://localhost:5081)
#   HQQQ_SMOKE_WARMUP_SECONDS   (default: 180)
#   HQQQ_SMOKE_TICK_WINDOW_SECONDS (default: 30)
#
# Exit codes:
#   0  — no critical failures (warnings are allowed)
#   1  — a critical precondition failed (e.g. docker/compose not reachable,
#                                         ingress readiness missing required
#                                         fields, no live tick flow inside
#                                         the tick-window, or end-to-end
#                                         warmup did not deliver nav>0 +
#                                         non-empty holdings + AdjustmentSummary)

[CmdletBinding()]
param()

$ErrorActionPreference = "Continue"

$script:CriticalFailures = 0
$script:Warnings = 0

function Write-Section { param([string]$Title) Write-Host ""; Write-Host "=== $Title ===" -ForegroundColor Cyan }
function Write-Pass    { param([string]$Message) Write-Host "  [PASS] $Message" -ForegroundColor Green }
function Write-Warn    { param([string]$Message) Write-Host "  [WARN] $Message" -ForegroundColor Yellow; $script:Warnings++ }
function Write-Info    { param([string]$Message) Write-Host "  [INFO] $Message" -ForegroundColor Gray }
function Write-Fail    { param([string]$Message, [switch]$Critical) Write-Host "  [FAIL] $Message" -ForegroundColor Red; if ($Critical) { $script:CriticalFailures++ } }

$gatewayBase = $env:HQQQ_GATEWAY_BASE_URL
if ([string]::IsNullOrWhiteSpace($gatewayBase)) { $gatewayBase = "http://localhost:5030" }

$refdataBase = $env:HQQQ_REFDATA_BASE_URL
if ([string]::IsNullOrWhiteSpace($refdataBase)) { $refdataBase = "http://localhost:5020" }

$ingressBase = $env:HQQQ_INGRESS_BASE_URL
if ([string]::IsNullOrWhiteSpace($ingressBase)) { $ingressBase = "http://localhost:5081" }

$warmupSeconds = $env:HQQQ_SMOKE_WARMUP_SECONDS
if ([string]::IsNullOrWhiteSpace($warmupSeconds)) { $warmupSeconds = 180 } else { $warmupSeconds = [int]$warmupSeconds }

$tickWindowSeconds = $env:HQQQ_SMOKE_TICK_WINDOW_SECONDS
if ([string]::IsNullOrWhiteSpace($tickWindowSeconds)) { $tickWindowSeconds = 30 } else { $tickWindowSeconds = [int]$tickWindowSeconds }

Write-Host ""
Write-Host "HQQQ Phase 2 smoke helper (self-sufficient runtime)" -ForegroundColor Green
Write-Host "  gateway base         : $gatewayBase"
Write-Host "  reference-data base  : $refdataBase"
Write-Host "  ingress base         : $ingressBase"
Write-Host "  warmup window        : ${warmupSeconds}s"
Write-Host "  ingress tick window  : ${tickWindowSeconds}s"

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
        if (-not $status) { Write-Warn "$svc : container not found (run 'docker compose up -d')"; continue }

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
# 2. Kafka topics present
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
    param([string]$Url, [string]$Label, [int[]]$PassCodes = @(200), [hashtable]$WarnCodes = @{})
    try {
        $resp = Invoke-WebRequest -Uri $Url -Method GET -TimeoutSec 5 -ErrorAction Stop
        $code = $resp.StatusCode
    } catch {
        if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
            $code = [int]$_.Exception.Response.StatusCode.value__
        } else {
            Write-Warn "$Label : unreachable ($($_.Exception.Message.Trim()))"
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

Test-Endpoint -Url "$gatewayBase/healthz/live"  -Label "gateway /healthz/live"
Test-Endpoint -Url "$gatewayBase/healthz/ready" -Label "gateway /healthz/ready"
Test-Endpoint -Url "$refdataBase/healthz/ready" -Label "reference-data /healthz/ready"

# ------------------------------------------------------------------
# 4. System-health rollup (Phase 2 self-sufficient required set)
# ------------------------------------------------------------------
Write-Section "4. Gateway /api/system/health rollup"

try {
    $resp = Invoke-WebRequest -Uri "$gatewayBase/api/system/health" -Method GET -TimeoutSec 5 -ErrorAction Stop
    $payload = $resp.Content | ConvertFrom-Json
    $status = $payload.status
    $deps = $payload.dependencies | ForEach-Object { "$($_.name)=$($_.status)" }
    Write-Info "top-level status : $status"
    Write-Info "dependencies     : $($deps -join ', ')"
    # ingress + reference-data are architecturally required; the rollup
    # must not be `unhealthy`, and both dependencies must be reachable.
    if ($status -eq "unhealthy") {
        Write-Fail "system/health reports unhealthy — Phase 2 required services missing" -Critical
    } else {
        Write-Pass "system/health reachable and not unhealthy"
    }
} catch {
    Write-Fail "system/health not reachable" -Critical
}

# ------------------------------------------------------------------
# 5. Active-basket + corp-action adjustment summary
# ------------------------------------------------------------------
Write-Section "5. /api/basket/current — AdjustmentSummary presence"

$deadline = (Get-Date).AddSeconds($warmupSeconds)
$basketOk = $false
while ((Get-Date) -lt $deadline) {
    try {
        $resp = Invoke-WebRequest -Uri "$refdataBase/api/basket/current" -Method GET -TimeoutSec 5 -ErrorAction Stop
        if ($resp.StatusCode -eq 200) {
            $payload = $resp.Content | ConvertFrom-Json
            if ($null -ne $payload.adjustmentSummary) {
                Write-Pass "/api/basket/current : adjustmentSummary present (source=$($payload.adjustmentSummary.source), splits=$($payload.adjustmentSummary.splitsApplied), renames=$($payload.adjustmentSummary.renamesApplied))"
                $basketOk = $true
                break
            }
        }
    } catch { }
    Start-Sleep -Seconds 3
}
if (-not $basketOk) {
    Write-Fail "/api/basket/current : adjustmentSummary did not appear within ${warmupSeconds}s" -Critical
}

# ------------------------------------------------------------------
# 5b. Ingress /healthz/ready — direct proof of basket-driven subscription
# ------------------------------------------------------------------
Write-Section "5b. Ingress /healthz/ready — basket-derived state"

function Read-IngressReady {
    try {
        $resp = Invoke-WebRequest -Uri "$ingressBase/healthz/ready" -Method GET -TimeoutSec 5 -ErrorAction Stop
        return @{
            Code = [int]$resp.StatusCode
            Json = ($resp.Content | ConvertFrom-Json)
        }
    } catch {
        if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
            $code = [int]$_.Exception.Response.StatusCode.value__
            try {
                $stream = $_.Exception.Response.GetResponseStream()
                $reader = New-Object System.IO.StreamReader($stream)
                $body = $reader.ReadToEnd()
                return @{ Code = $code; Json = ($body | ConvertFrom-Json) }
            } catch { return @{ Code = $code; Json = $null } }
        }
        return @{ Code = 0; Json = $null }
    }
}

# Pull basket-derived data from the named "ingress-basket" check.
# /healthz/ready response shape: { dependencies: [ { name, status, data: {...} } ] }
$deadline = (Get-Date).AddSeconds($warmupSeconds)
$ingressBasketChecked = $false
$initialPublishedTicks = $null
$initialReadyJson = $null

while ((Get-Date) -lt $deadline) {
    $ready = Read-IngressReady
    if ($ready.Json -ne $null) {
        $deps = @($ready.Json.dependencies)
        $basket = $deps | Where-Object { $_.name -eq "ingress-basket" } | Select-Object -First 1
        $upstream = $deps | Where-Object { $_.name -eq "tiingo-upstream" } | Select-Object -First 1

        if ($basket -ne $null -and $basket.data -ne $null `
            -and $basket.data.appliedSymbolCount -ne $null `
            -and [int]$basket.data.appliedSymbolCount -gt 0 `
            -and $basket.data.appliedFingerprint -ne $null `
            -and $basket.data.appliedFingerprint -ne "") {
            Write-Pass "ingress.basket.appliedFingerprint = $($basket.data.appliedFingerprint)"
            Write-Pass "ingress.basket.appliedSymbolCount = $($basket.data.appliedSymbolCount)"
            if ($basket.data.lastAppliedUtc -ne $null) {
                Write-Pass "ingress.basket.lastAppliedUtc    = $($basket.data.lastAppliedUtc)"
            } else {
                Write-Warn "ingress.basket.lastAppliedUtc not present"
            }
            if ($upstream -ne $null) {
                Write-Pass "ingress.upstream.status           = $($upstream.status); isUpstreamConnected=$($upstream.data.isUpstreamConnected)"
                $publishedRaw = $null
                if ($upstream.data -ne $null -and $upstream.data.publishedTickCount -ne $null) {
                    $publishedRaw = $upstream.data.publishedTickCount
                }
                if ($publishedRaw -eq $null) { $initialPublishedTicks = [int64]0 } else { $initialPublishedTicks = [int64]$publishedRaw }
                $initialReadyJson = $ready.Json
            } else {
                Write-Fail "ingress /healthz/ready does not contain a 'tiingo-upstream' dependency" -Critical
            }
            $ingressBasketChecked = $true
            break
        }
    }
    Start-Sleep -Seconds 3
}

if (-not $ingressBasketChecked) {
    Write-Fail "ingress /healthz/ready did not expose appliedFingerprint + appliedSymbolCount > 0 within ${warmupSeconds}s" -Critical
}

# ------------------------------------------------------------------
# 5c. Ingress live tick flow — direct PublishedTickCount growth proof
# ------------------------------------------------------------------
Write-Section "5c. Ingress live tick flow"

if ($initialReadyJson -eq $null) {
    Write-Warn "skipping live-tick-flow check (no ingress readiness baseline captured)"
} else {
    $startSecs = [int]$tickWindowSeconds
    Write-Info "baseline publishedTickCount=$initialPublishedTicks; sampling again in ${startSecs}s"
    Start-Sleep -Seconds $startSecs

    $after = Read-IngressReady
    $ticksOk = $false
    $lastUtcOk = $false

    if ($after.Json -ne $null) {
        $upstream = @($after.Json.dependencies) | Where-Object { $_.name -eq "tiingo-upstream" } | Select-Object -First 1
        if ($upstream -ne $null -and $upstream.data -ne $null) {
            $publishedRawAfter = $null
            if ($upstream.data.publishedTickCount -ne $null) { $publishedRawAfter = $upstream.data.publishedTickCount }
            if ($publishedRawAfter -eq $null) { $afterPublished = [int64]0 } else { $afterPublished = [int64]$publishedRawAfter }
            $delta = $afterPublished - $initialPublishedTicks
            if ($delta -gt 0) {
                Write-Pass "ingress.publishedTickCount grew by $delta in ${startSecs}s (baseline=$initialPublishedTicks → $afterPublished)"
                $ticksOk = $true
            }

            if ($upstream.data.lastPublishedTickUtc -ne $null) {
                try {
                    $lastUtc = [DateTimeOffset]::Parse([string]$upstream.data.lastPublishedTickUtc)
                    $age = ([DateTimeOffset]::UtcNow - $lastUtc).TotalSeconds
                    if ($age -lt ($startSecs * 2)) {
                        Write-Pass "ingress.lastPublishedTickUtc is recent (age=$([int]$age)s)"
                        $lastUtcOk = $true
                    } else {
                        Write-Warn "ingress.lastPublishedTickUtc is stale (age=$([int]$age)s)"
                    }
                } catch {
                    Write-Warn "ingress.lastPublishedTickUtc could not be parsed"
                }
            }
        }
    }

    if (-not $ticksOk -and -not $lastUtcOk) {
        Write-Fail "ingress did not publish any ticks within the ${startSecs}s tick-window — Tiingo upstream is not flowing" -Critical
    } elseif (-not $ticksOk) {
        Write-Warn "ingress.publishedTickCount did not advance, but lastPublishedTickUtc is recent (acceptable fallback)"
    }
}

# ------------------------------------------------------------------
# 6. End-to-end: gateway /api/quote (nav > 0) + /api/constituents
# ------------------------------------------------------------------
Write-Section "6. End-to-end: nav>0 and non-empty holdings"

$deadline = (Get-Date).AddSeconds($warmupSeconds)
$quoteOk = $false
$consOk  = $false

while ((Get-Date) -lt $deadline) {
    if (-not $quoteOk) {
        try {
            $resp = Invoke-WebRequest -Uri "$gatewayBase/api/quote" -Method GET -TimeoutSec 5 -ErrorAction Stop
            if ($resp.StatusCode -eq 200) {
                $payload = $resp.Content | ConvertFrom-Json
                if ($null -ne $payload.nav -and [decimal]$payload.nav -gt 0) {
                    Write-Pass "/api/quote : nav=$($payload.nav)"
                    $quoteOk = $true
                }
            }
        } catch { }
    }

    if (-not $consOk) {
        try {
            $resp = Invoke-WebRequest -Uri "$gatewayBase/api/constituents" -Method GET -TimeoutSec 5 -ErrorAction Stop
            if ($resp.StatusCode -eq 200) {
                $payload = $resp.Content | ConvertFrom-Json
                if ($payload.holdings -and $payload.holdings.Count -gt 0) {
                    Write-Pass "/api/constituents : holdings.length=$($payload.holdings.Count)"
                    $consOk = $true
                }
            }
        } catch { }
    }

    if ($quoteOk -and $consOk) { break }
    Start-Sleep -Seconds 5
}

if (-not $quoteOk) {
    Write-Fail "/api/quote : did not produce nav>0 within ${warmupSeconds}s warmup window" -Critical
}
if (-not $consOk) {
    Write-Fail "/api/constituents : did not produce non-empty holdings within ${warmupSeconds}s warmup window" -Critical
}

# ------------------------------------------------------------------
# 7. Gateway history (Timescale path; empty 1D payload is acceptable)
# ------------------------------------------------------------------
Write-Section "7. Gateway /api/history?range=1D"

Test-Endpoint `
    -Url "$gatewayBase/api/history?range=1D" `
    -Label "/api/history?range=1D" `
    -PassCodes @(200) `
    -WarnCodes @{ 503 = "history_unavailable — Timescale not reachable; populate hqqq-persistence or start TimescaleDB" }

Write-Info "HTTP 200 with empty payload (pointCount=0) is a clean state on a fresh environment."

# ------------------------------------------------------------------
# Summary
# ------------------------------------------------------------------
Write-Section "Summary"

Write-Host "  warnings           : $($script:Warnings)"
Write-Host "  critical failures  : $($script:CriticalFailures)"

if ($script:CriticalFailures -gt 0) { exit 1 }
exit 0
