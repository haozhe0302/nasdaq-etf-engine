# Phase 2 — bring up the full self-sufficient local stack (infra + app tier).
#
# Thin wrapper around `docker compose` with the two-file overlay. The legacy
# `hqqq-api` monolith is NOT started and NOT required — Phase 2 runtime is
# self-sufficient. Runs from the repo root regardless of where the user
# invokes it from. Excludes the `analytics` profile (one-shot job; see
# docs/phase2/local-dev.md).
#
# Usage:
#   .\scripts\phase2-up.ps1            # build + up -d
#   .\scripts\phase2-up.ps1 -NoBuild   # up -d only (use existing images)
#
# Prerequisites:
#   - Tiingo__ApiKey must be set (via .env or environment). hqqq-ingress
#     fails fast on a missing/placeholder API key.

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
    Write-Host "Stack is starting. Next steps:" -ForegroundColor Green
    Write-Host "  1. Wait for kafka health (docker compose ps)"
    Write-Host "  2. Bootstrap topics:  .\scripts\bootstrap-kafka-topics.ps1"
    Write-Host "  3. Smoke check:       .\scripts\phase2-smoke.ps1"
    Write-Host ""
    Write-Host "Endpoints:"
    Write-Host "  gateway          : http://localhost:5030"
    Write-Host "  reference-data   : http://localhost:5020/healthz/ready"
    Write-Host "  ingress mgmt     : http://localhost:5081/healthz/ready"
    Write-Host "  quote-engine mgmt: http://localhost:5082/healthz/ready"
    Write-Host "  persistence mgmt : http://localhost:5083/healthz/ready"
}
finally {
    Pop-Location
}
