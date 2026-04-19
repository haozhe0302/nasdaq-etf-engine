# Phase 2D5 — bring up the replica-smoke stack (infra + app tier + 2nd gateway).
#
# Layered three-file compose: infra base + Phase 2 app overlay +
# replica-smoke overlay (adds hqqq-gateway-b on host port 5031).
# Excludes the `analytics` profile (one-shot job; see local-dev.md).
#
# Usage:
#   .\scripts\replica-smoke-up.ps1            # build + up -d
#   .\scripts\replica-smoke-up.ps1 -NoBuild   # up -d only (use existing images)

param(
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    $composeArgs = @(
        "compose",
        "-f", "docker-compose.yml",
        "-f", "docker-compose.phase2.yml",
        "-f", "docker-compose.replica-smoke.yml",
        "up", "-d"
    )
    if (-not $NoBuild) {
        $composeArgs += "--build"
    }

    Write-Host "==> docker $($composeArgs -join ' ')" -ForegroundColor Cyan
    & docker @composeArgs
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose up failed (exit $LASTEXITCODE)"
    }

    Write-Host ""
    Write-Host "Replica-smoke stack is starting. Next steps:" -ForegroundColor Green
    Write-Host "  1. Wait for kafka health (docker compose ps)"
    Write-Host "  2. Bootstrap topics:  .\scripts\bootstrap-kafka-topics.ps1"
    Write-Host "  3. Run replica smoke: .\scripts\replica-smoke.ps1"
    Write-Host ""
    Write-Host "Endpoints:"
    Write-Host "  gateway-a (hqqq-gateway)   : http://localhost:5030"
    Write-Host "  gateway-b (hqqq-gateway-b) : http://localhost:5031"
    Write-Host "  reference-data             : http://localhost:5020/healthz/ready"
    Write-Host "  ingress mgmt               : http://localhost:5081/healthz/ready"
    Write-Host "  quote-engine mgmt          : http://localhost:5082/healthz/ready"
    Write-Host "  persistence mgmt           : http://localhost:5083/healthz/ready"
}
finally {
    Pop-Location
}
