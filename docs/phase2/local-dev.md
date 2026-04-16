# Phase 2 — Local Development Guide

## Prerequisites

- **.NET 10 SDK** — see `global.json` for the exact version
- **Docker Desktop** (or compatible Docker/Compose runtime)
- **Git**
- A Tiingo API key (free tier sufficient) if you intend to test ingress

Verify your SDK:

```
dotnet --version
```

## Setup

### 1. Copy the environment template

```powershell
Copy-Item .env.example .env
```

Edit `.env` and fill in your Tiingo API key if needed:

```
Tiingo__ApiKey=your-real-key-here
```

All other values have sensible local-dev defaults.

### 2. Start infrastructure

```powershell
docker compose up -d
```

This starts:
- **TimescaleDB** (PostgreSQL) on port `5432`
- **Redis** on port `6379`
- **Kafka** (KRaft mode, no ZooKeeper) on port `9092`
- **Kafka UI** on port `8080`
- **Prometheus** on port `9090`
- **Grafana** on port `3000`

Wait for all containers to be healthy:

```powershell
docker compose ps
```

### 3. Bootstrap Kafka topics

Topics are **not** auto-created. Run the bootstrap script after Kafka is healthy:

```powershell
# PowerShell
.\scripts\bootstrap-kafka-topics.ps1

# Bash
./scripts/bootstrap-kafka-topics.sh
```

This creates the following topics (idempotent — safe to run multiple times):

| Topic | Partitions | Cleanup Policy |
|-------|------------|---------------|
| `market.raw_ticks.v1` | 3 | delete |
| `market.latest_by_symbol.v1` | 3 | compact |
| `refdata.basket.active.v1` | 1 | compact |
| `refdata.basket.events.v1` | 1 | delete |
| `pricing.snapshots.v1` | 1 | delete |
| `ops.incidents.v1` | 1 | delete |

### 4. Verify infrastructure

```powershell
# Redis
docker exec cache redis-cli ping
# Expected: PONG

# PostgreSQL / TimescaleDB
docker exec db pg_isready -U admin
# Expected: accepting connections

# Kafka — list topics
docker exec kafka /opt/kafka/bin/kafka-topics.sh --bootstrap-server localhost:9092 --list
# Expected: all 6 topics listed

# Prometheus
curl http://localhost:9090/-/healthy
# Expected: Prometheus Server is Healthy.

# Grafana
curl http://localhost:3000/api/health
# Expected: {"commit":"...","database":"ok","version":"..."}
```

Or open in browser:
- Kafka UI: http://localhost:8080
- Prometheus: http://localhost:9090
- Grafana: http://localhost:3000 (admin / changeme)

## Running Phase 2 services

Build the entire solution first:

```powershell
dotnet build Hqqq.sln
```

Run individual services:

```powershell
# Web services (with ports from launchSettings or override)
dotnet run --project src/services/hqqq-reference-data
dotnet run --project src/services/hqqq-gateway

# Worker services
dotnet run --project src/services/hqqq-ingress
dotnet run --project src/services/hqqq-quote-engine
dotnet run --project src/services/hqqq-persistence
dotnet run --project src/services/hqqq-analytics
```

Each service will:
1. Log its configuration posture (which config sections are set vs defaults)
2. Report dependency health as degraded if infra is unreachable
3. Enter an idle loop (no business logic is wired yet)

### Running the legacy API

The Phase 1 monolith still works independently:

```powershell
dotnet run --project src/hqqq-api
```

## Running tests

```powershell
dotnet test Hqqq.sln
```

## What works now vs. what is stubbed

### Working now (Phase 2A)
- Infrastructure containers start and are healthy
- Kafka topics are bootstrapped with correct partition/compaction settings
- All services compile, bind configuration, and start without crashing
- Health endpoints return structured JSON (`/healthz/live`, `/healthz/ready`)
- Shared libraries provide real connection factories and config builders
- Configuration uses hierarchical .NET config (`Tiingo__ApiKey`, `Kafka__BootstrapServers`, etc.)
- Legacy flat env vars are auto-mapped with deprecation warnings

### Intentionally stubbed (Phase 2B/C)
- No real Tiingo websocket or REST data ingestion
- No Kafka message publishing or consumption
- No iNAV quote calculation
- No Redis snapshot writes or reads
- No TimescaleDB data persistence
- No replay, backfill, or anomaly detection jobs
- Gateway `/api/quote` returns 503 "not yet wired"
- Workers idle in 5-second delay loops

These stubs will be replaced incrementally in Phase 2B and 2C.

## Troubleshooting

**Kafka topics not creating:** Ensure the Kafka container is fully healthy
before running the bootstrap script. Check `docker compose ps` — Kafka's
health check has a 30-second start period.

**Services can't connect to infra:** Verify `.env` has correct defaults
(`localhost:9092`, `localhost:6379`, etc.) and that Docker containers are
running.

**Build fails with SDK error:** This repo requires .NET 10 SDK. Check
`global.json` for the exact version and install from https://dotnet.microsoft.com.

**.env not loaded:** Phase 2 services use standard .NET configuration
providers. Set env vars directly, or use `dotnet user-secrets` for local
overrides. The `.env` file is loaded by Docker Compose for container config.
