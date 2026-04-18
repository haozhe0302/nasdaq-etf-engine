# Bootstrap Kafka topics for local development.
# Requires: docker compose infra running (kafka container healthy).
# Usage: .\scripts\bootstrap-kafka-topics.ps1
#
# Idempotent — safe to run multiple times.

$ErrorActionPreference = "Stop"

$BootstrapServer = "localhost:9092"
$TopicCmd = "/opt/kafka/bin/kafka-topics.sh"

function Invoke-KafkaTopic {
    param(
        [string]$Name,
        [int]$Partitions = 1,
        [string]$CleanupPolicy = "delete"
    )

    $configArgs = @("--config", "cleanup.policy=$CleanupPolicy")
    if ($CleanupPolicy -eq "compact") {
        $configArgs += @("--config", "min.cleanable.dirty.ratio=0.1")
        $configArgs += @("--config", "segment.ms=100")
    }

    Write-Host "  Creating topic: $Name (partitions=$Partitions, cleanup=$CleanupPolicy)" -ForegroundColor Cyan

    $allArgs = @(
        "compose", "exec", "-T", "kafka", $TopicCmd,
        "--bootstrap-server", $BootstrapServer,
        "--create",
        "--if-not-exists",
        "--topic", $Name,
        "--partitions", $Partitions,
        "--replication-factor", 1
    ) + $configArgs

    docker @allArgs 2>&1 | ForEach-Object { Write-Host "    $_" }

    if ($LASTEXITCODE -ne 0) {
        Write-Host "  WARNING: topic $Name may already exist or creation failed." -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "=== HQQQ Kafka Topic Bootstrap ===" -ForegroundColor Green
Write-Host ""

docker compose ps kafka 1>$null 2>$null
if ($LASTEXITCODE -ne 0) {
    throw "Kafka service 'kafka' not found. Run this script from repo root and ensure 'docker compose up -d' has started infrastructure."
}

Invoke-KafkaTopic -Name "market.raw_ticks.v1"            -Partitions 3 -CleanupPolicy "delete"
Invoke-KafkaTopic -Name "market.latest_by_symbol.v1"      -Partitions 3 -CleanupPolicy "compact"
Invoke-KafkaTopic -Name "refdata.basket.active.v1"        -Partitions 1 -CleanupPolicy "compact"
Invoke-KafkaTopic -Name "refdata.basket.events.v1"        -Partitions 1 -CleanupPolicy "delete"
Invoke-KafkaTopic -Name "pricing.snapshots.v1"            -Partitions 1 -CleanupPolicy "delete"
Invoke-KafkaTopic -Name "ops.incidents.v1"                -Partitions 1 -CleanupPolicy "delete"

Write-Host ""
Write-Host "=== Topic bootstrap complete ===" -ForegroundColor Green
Write-Host ""

Write-Host "Verifying topics:" -ForegroundColor Cyan
docker compose exec -T kafka $TopicCmd --bootstrap-server $BootstrapServer --list
