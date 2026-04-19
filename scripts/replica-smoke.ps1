# Phase 2D5 — run the multi-gateway replica-smoke harness.
#
# Wrapper around the Hqqq.Gateway.ReplicaSmoke console exe. Probes REST on
# both gateways, opens two SignalR clients (one per gateway), publishes a
# single QuoteUpdate to Redis, and asserts both clients receive it.
#
# Usage:
#   .\scripts\replica-smoke.ps1
#
# Environment overrides (defaults match docker-compose.replica-smoke.yml):
#   HQQQ_GATEWAY_A_BASE_URL          (default: http://localhost:5030)
#   HQQQ_GATEWAY_B_BASE_URL          (default: http://localhost:5031)
#   Redis__Configuration             (default: localhost:6379)
#   Gateway__BasketId                (default: HQQQ)
#   HQQQ_REPLICA_SMOKE_TIMEOUT_SECONDS (default: 15)
#
# Exit codes:
#   0 — REST + SignalR fan-out verified on both replicas.
#   1 — anything else (REST failure, SignalR connect failure, missed
#       message on either client, payload mismatch).

$ErrorActionPreference = "Continue"

$repoRoot = Split-Path -Parent $PSScriptRoot
$harnessProject = Join-Path $repoRoot "tests\Hqqq.Gateway.ReplicaSmoke\Hqqq.Gateway.ReplicaSmoke.csproj"

Push-Location $repoRoot
try {
    Write-Host "==> dotnet run --project $harnessProject -c Release" -ForegroundColor Cyan
    dotnet run --project $harnessProject -c Release
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
