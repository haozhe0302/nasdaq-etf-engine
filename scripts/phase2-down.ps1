# Phase 2 — tear down the local stack (infra + app tier).
#
# Default behavior preserves named volumes (Timescale data, redis data,
# quote-engine checkpoint, prometheus/grafana state). Pass -RemoveVolumes
# to also drop them.
#
# Usage:
#   .\scripts\phase2-down.ps1                          # stop + remove containers/networks
#   .\scripts\phase2-down.ps1 -RemoveVolumes           # also drop named volumes (data loss)
#   .\scripts\phase2-down.ps1 -IncludeReplicaSmoke     # also tear down the D5 replica-smoke overlay
#   .\scripts\phase2-down.ps1 -IncludeReplicaSmoke -RemoveVolumes

param(
    [switch]$RemoveVolumes,
    [switch]$IncludeReplicaSmoke
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    $composeArgs = @(
        "compose",
        "-f", "docker-compose.yml",
        "-f", "docker-compose.phase2.yml"
    )
    if ($IncludeReplicaSmoke) {
        $composeArgs += @("-f", "docker-compose.replica-smoke.yml")
    }
    $composeArgs += "down"
    if ($RemoveVolumes) {
        $composeArgs += "-v"
        Write-Warning "Removing named volumes — Timescale, Redis, and quote-engine checkpoint will be lost."
    }

    Write-Host "==> docker $($composeArgs -join ' ')" -ForegroundColor Cyan
    & docker @composeArgs
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose down failed (exit $LASTEXITCODE)"
    }
}
finally {
    Pop-Location
}
