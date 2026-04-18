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
```

```bash
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

## 5. Build and service startup order

Build the entire solution once:

```powershell
dotnet build Hqqq.sln
```

Run the Phase 2 services in the order below. Each step is an independent
terminal / process — no single orchestrator starts them all today.

```powershell
# 1. Reference data (basket registry; in-memory seed today)
dotnet run --project src/services/hqqq-reference-data

# 2. Quote engine (Kafka consumer + iNAV compute + Redis + pricing.snapshots.v1)
dotnet run --project src/services/hqqq-quote-engine

# 3. Ingress (still stub; run only when exercising the host)
dotnet run --project src/services/hqqq-ingress

# 4. Persistence (pricing.snapshots.v1 + market.raw_ticks.v1 → TimescaleDB)
dotnet run --project src/services/hqqq-persistence

# 5. Gateway (REST + SignalR; source selection via Gateway:Sources:*)
dotnet run --project src/services/hqqq-gateway

# 6. Analytics — one-shot report job, run on demand, not continuously
#    See Section 7 for the required Analytics__* env vars.
dotnet run --project src/services/hqqq-analytics
```

Startup order rationale:

1. `hqqq-reference-data` publishes/holds the active basket that downstream
   services key off.
2. `hqqq-quote-engine` starts before the gateway so that, in B5 Redis mode,
   the first `/api/quote` / `/api/constituents` request can find a snapshot
   in Redis. It also produces `pricing.snapshots.v1`.
3. `hqqq-ingress` is a placeholder — live Tiingo ingestion today is still
   done by the legacy `hqqq-api` monolith.
4. `hqqq-persistence` consumes both Kafka topics and is the only writer of
   Timescale history that the gateway's C2 mode reads.
5. `hqqq-gateway` is the serving edge and is last so its probes come up
   against a populated Redis / Timescale state.
6. `hqqq-analytics` is a one-shot job; run it only when you actually want
   a report.

## 6. Operating modes for the gateway

The gateway supports three realistic local-dev modes, all configured via
`Gateway:DataSource` plus per-endpoint `Gateway:Sources:*` overrides.
See [../../src/services/hqqq-gateway/README.md](../../src/services/hqqq-gateway/README.md)
for the full matrix.

### Legacy proxy mode (parity with Phase 1)

Requires `hqqq-api` running.

```powershell
$env:Gateway__DataSource = "legacy"
$env:Gateway__LegacyBaseUrl = "http://localhost:5000"
dotnet run --project src/services/hqqq-gateway
```

### Mixed B5 + C2 cutover mode (recommended)

Serve live quote/constituents from Redis and history from Timescale. Requires
Redis + `hqqq-quote-engine` running (for Redis snapshots) and TimescaleDB +
`hqqq-persistence` running (for history rows). System-health still stays on
stub or legacy forwarding.

```powershell
$env:Gateway__DataSource = "legacy"                # or "stub"
$env:Gateway__LegacyBaseUrl = "http://localhost:5000"  # only if DataSource=legacy
$env:Gateway__Sources__Quote = "redis"
$env:Gateway__Sources__Constituents = "redis"
$env:Gateway__Sources__History = "timescale"
$env:Gateway__BasketId = "HQQQ"
$env:Redis__Configuration = "localhost:6379"
$env:Timescale__ConnectionString = "Host=localhost;Port=5432;Database=hqqq;Username=admin;Password=changeme"
dotnet run --project src/services/hqqq-gateway
```

### Stub mode (UI smoke / offline)

```powershell
$env:Gateway__DataSource = "stub"
dotnet run --project src/services/hqqq-gateway
```

## 7. Running an analytics report

`hqqq-analytics` is a **one-shot job**, not a long-running service. It
reads persisted Timescale data, prints a summary, optionally writes a JSON
artifact, and exits. The required env vars are `StartUtc` and `EndUtc`.

```powershell
$env:Timescale__ConnectionString = "Host=localhost;Port=5432;Database=hqqq;Username=admin;Password=changeme"
$env:Analytics__Mode      = "report"
$env:Analytics__BasketId  = "HQQQ"
$env:Analytics__StartUtc  = "2026-04-17T00:00:00Z"
$env:Analytics__EndUtc    = "2026-04-18T00:00:00Z"
$env:Analytics__EmitJsonPath = "artifacts/hqqq-report.json"   # optional
$env:Analytics__IncludeRawTickAggregates = "false"            # optional

dotnet run --project src/services/hqqq-analytics/hqqq-analytics.csproj
```

Exit codes: `0` success (including empty-window), `1` job failure, `2`
unsupported `Analytics:Mode`. An empty window is **not** a failure — the
host logs a single `WARN`, emits `hasData=false`, and exits cleanly.

## 8. Running the legacy API

The Phase 1 monolith still works independently and is currently the only
source of real Tiingo ingestion, basket refresh, and `/api/system/health`
aggregation:

```powershell
dotnet run --project src/hqqq-api
```

## 9. Running tests

```powershell
dotnet test Hqqq.sln
```

## 10. What is current vs. what is still deferred

### Current (Phase 2 through C4)
- Infrastructure containers start healthy; topics bootstrapped at 3/3/1/1/1/1
  partitions.
- `hqqq-reference-data` exposes an in-memory HQQQ basket via its REST surface.
- `hqqq-quote-engine` consumes `market.raw_ticks.v1` + `refdata.basket.active.v1`,
  computes iNAV, writes Redis snapshot + constituents, publishes
  `pricing.snapshots.v1`.
- `hqqq-persistence` consumes `pricing.snapshots.v1` + `market.raw_ticks.v1`
  into `quote_snapshots` and `raw_ticks`; bootstraps hypertables, rollups,
  and retention policies at startup.
- `hqqq-gateway` serves `/api/quote` + `/api/constituents` from Redis and
  `/api/history?range=` from Timescale when configured.
- `hqqq-analytics` runs a deterministic one-shot report over Timescale.
- Health endpoints (`/healthz/live`, `/healthz/ready`) return structured JSON
  on every web service.

### Still deferred
- Real Tiingo ingestion in `hqqq-ingress` (still served by the legacy
  monolith).
- Real issuer-feed + corporate-action pipeline in `hqqq-reference-data`.
- Gateway-native `/api/system/health` aggregation (still stub / legacy
  forwarding).
- SignalR Redis backplane on `/hubs/market` — Phase 2D2.
- Multi-replica / HA infra — Phase 2D3.
- Replay / backfill / anomaly detection in `hqqq-analytics` — Phase 2C5+.

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
