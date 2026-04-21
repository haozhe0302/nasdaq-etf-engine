# Phase 2 — Local Development Guide

## Prerequisites

- **.NET 10 SDK** pinned to the exact version in `global.json`
- **Docker Desktop** (or compatible Docker/Compose runtime)
- **Git**
- **A Tiingo API key** (free tier sufficient) — **required** for the
  Phase 2 app tier. `hqqq-ingress` fails fast at startup if the key is
  missing or a placeholder; there is no stub / hybrid / log-only path.

### Required .NET SDK

This repository pins the SDK via `global.json`:

```json
{
  "sdk": {
    "version": "10.0.202",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
```

Install the pinned stable SDK (or any later `10.0.2xx` feature band) —
see the [.NET downloads page](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).
An RC / preview build such as `10.0.100-rc.2` will intentionally fail SDK
resolution because `allowPrerelease=false`; this is by design for
reproducibility. Do **not** relax `global.json` — fix the environment.

Verify your SDK:

```
dotnet --version
# expected: 10.0.202 (or a higher 10.0.2xx feature-band stable release)

dotnet --list-sdks
# expected to include: 10.0.202 [<install path>]
```

If `dotnet --version` errors with
`Requested SDK version: 10.0.202 … not found`, the runner has only an
RC/preview SDK installed. Resolve **by installing the pinned SDK**
(below), not by relaxing `global.json`:

- **Windows / macOS:** install the latest `10.0.2xx` stable release from
  <https://dotnet.microsoft.com/en-us/download/dotnet/10.0>.
- **Linux:** use the upstream installer script with the same pin —
  `curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --version 10.0.202`.

Once installed, re-run `dotnet --list-sdks` to confirm `10.0.202`
appears, then re-run the build/test commands.

CI (`.github/workflows/phase2-ci.yml`) uses the same pin via
`actions/setup-dotnet@v4` with `global-json-file: global.json` and
fails the build with an explicit error if the resolved SDK is not in
the `10.0.2xx` feature band — local and CI toolchains stay in lockstep.

To execute the full Phase 2 test suite locally exactly as CI does:

```
dotnet restore Hqqq.sln
dotnet build   Hqqq.sln --no-restore --configuration Release
dotnet test    Hqqq.sln --no-build   --configuration Release --verbosity normal
```

## Setup

### 1. Copy the environment template

```powershell
Copy-Item .env.example .env
```

Edit `.env` and fill in your Tiingo API key — **required** for Phase 2:

```
Tiingo__ApiKey=your-real-key-here
```

All other values have sensible local-dev defaults. The legacy
`hqqq-api` monolith is NOT required to run Phase 2 — `scripts/phase2-up.{ps1,sh}`
never start it.

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
# 1. Reference data (active basket owner; composite holdings + corp-actions)
dotnet run --project src/services/hqqq-reference-data

# 2. Quote engine (Kafka consumer + iNAV compute + Redis + pricing.snapshots.v1)
dotnet run --project src/services/hqqq-quote-engine

# 3. Ingress (real Tiingo IEX websocket + REST snapshot warmup; basket-driven
#    subscription. Requires Tiingo__ApiKey — fails fast otherwise.)
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

1. `hqqq-reference-data` activates and publishes the active basket
   (`refdata.basket.active.v1`) that downstream services key off.
2. `hqqq-quote-engine` starts before the gateway so that, in Redis-mode,
   the first `/api/quote` / `/api/constituents` request can find a
   snapshot in Redis. It also produces `pricing.snapshots.v1`.
3. `hqqq-ingress` consumes the active basket, opens the Tiingo IEX
   websocket, and publishes `market.raw_ticks.v1` +
   `market.latest_by_symbol.v1`. Subscriptions are driven by
   `BasketActiveStateV1.Constituents`; static `Tiingo:Symbols` is a
   bootstrap fallback only.
4. `hqqq-persistence` consumes both market topics and is the only
   writer of the Timescale history that the gateway reads in
   `Gateway:Sources:History=timescale`.
5. `hqqq-gateway` is the serving edge and is last so its probes come
   up against a populated Redis / Timescale state. The default
   per-endpoint posture is `redis` / `redis` / `timescale` /
   `aggregated` — the legacy `hqqq-api` monolith is **not** required.
6. `hqqq-analytics` is a one-shot job; run it only when you actually
   want a report.

## 6. Operating modes for the gateway

The gateway's default Phase 2 posture is the per-endpoint mix
(`Gateway:Sources:Quote=redis`, `…Constituents=redis`, `…History=timescale`,
`…SystemHealth=aggregated`). The full matrix lives in
[../../src/services/hqqq-gateway/README.md](../../src/services/hqqq-gateway/README.md).

### Default Phase 2 posture (recommended)

No `hqqq-api` involvement. Requires Redis + `hqqq-quote-engine` (for
Redis snapshots) and TimescaleDB + `hqqq-persistence` (for history rows).

```powershell
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

### Legacy proxy mode (legacy parity only — NOT a Phase 2 path)

Forwards to a separately-running `hqqq-api` monolith. The Phase 2
default never selects this; it remains in the codebase only for
side-by-side parity testing during a future regression bisect. The
gateway logs a loud warning at startup when any endpoint resolves to
`legacy`.

```powershell
$env:Gateway__DataSource = "legacy"
$env:Gateway__LegacyBaseUrl = "http://localhost:5000"
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

## 8. Running the legacy API (optional, parity only)

The Phase 1 monolith still compiles and runs independently and continues
to back the public live demo links in the root `README.md`. It is **not
required** to run Phase 2 — `hqqq-ingress`, `hqqq-reference-data`, the
gateway aggregator, and corp-action adjustment are all owned by Phase 2
services. Run the monolith only for legacy parity testing:

```powershell
dotnet run --project src/hqqq-api
```

## 9. Running tests

```powershell
dotnet test Hqqq.sln
```

## 10. What is current vs. what is still deferred

### Current (Phase 2 through D5)
- Infrastructure containers start healthy; topics bootstrapped at 3/3/1/1/1/1
  partitions.
- `hqqq-reference-data` exposes an in-memory HQQQ basket via its REST surface.
- `hqqq-quote-engine` consumes `market.raw_ticks.v1` + `refdata.basket.active.v1`,
  computes iNAV, writes Redis snapshot + constituents, publishes
  `pricing.snapshots.v1`, **and** publishes live `QuoteUpdate` envelopes
  to the Redis pub/sub channel `hqqq:channel:quote-update` (D2).
- `hqqq-persistence` consumes `pricing.snapshots.v1` + `market.raw_ticks.v1`
  into `quote_snapshots` and `raw_ticks`; bootstraps hypertables, rollups,
  and retention policies at startup.
- `hqqq-gateway` serves `/api/quote` + `/api/constituents` from Redis,
  `/api/history?range=` from Timescale, and `/api/system/health` from
  the **native aggregator** (D1) that scrapes each Phase 2 worker's
  `/healthz/ready` plus the local Redis/Timescale probes.
- `hqqq-gateway` `/hubs/market` is multi-replica-safe (D2 + D5): every
  replica subscribes to `hqqq:channel:quote-update` and broadcasts
  locally; SignalR Redis backplane is deliberately not enabled.
- `hqqq-analytics` runs a deterministic one-shot report over Timescale.
- Health endpoints (`/healthz/live`, `/healthz/ready`) return structured JSON
  on every web service.
- Containerized app tier (D3): `docker-compose.phase2.yml` + per-service
  Dockerfiles + `phase2-up`/`phase2-smoke`/`phase2-down` wrappers.
- Azure Container Apps deployment (D4): `infra/azure/` Bicep + GitHub
  OIDC workflows. See [azure-deploy.md](azure-deploy.md).
- Multi-gateway replica smoke (D5): `docker-compose.replica-smoke.yml`
  + `tests/Hqqq.Gateway.ReplicaSmoke/` (see Section 12).

### Still deferred
- Provider-specific holdings scrape adapters (Schwab / StockAnalysis /
  AlphaVantage) — `hqqq-reference-data` runs on `File` / `Http` drops
  plus the deterministic fallback seed today; those scrapers stay in
  the monolith as reference until they are ported behind
  `IHoldingsSource`.
- Wider corp-action coverage: Phase 2 implements forward / reverse
  splits, ticker renames, constituent transition detection, and
  scale-factor continuity. Dividends, spin-offs, mergers, and
  cross-exchange moves are intentionally out of scope.
- Replay / backfill / anomaly detection in `hqqq-analytics`.
- HA topologies for Kafka / Redis / Timescale; multi-instance
  quote-engine / persistence / ingress / reference-data.
- Kubernetes app-tier operationalization — Phase 3.

## 11. Containerized Phase 2 stack (D3)

In addition to running each service via `dotnet run`, the entire Phase 2
app tier can be brought up as containers on top of the existing infra
compose. This is the deployment-shaped local path; the host-`dotnet run`
path remains supported for hot-reload and native debugging.

> Cloud counterpart of this same compose layout: see
> [azure-deploy.md](azure-deploy.md). The Phase 2 services run unchanged
> on Azure Container Apps using the same hierarchical config keys
> (`Kafka__BootstrapServers`, `Redis__Configuration`,
> `Timescale__ConnectionString`, `Gateway__*`).

### Layout

| File | Role |
|------|------|
| `docker-compose.yml` | Infra base: TimescaleDB, Redis, Kafka, Kafka UI, Prometheus, Grafana. |
| `docker-compose.phase2.yml` | App-tier overlay: reference-data, ingress, quote-engine, persistence, gateway, analytics (profile). |
| `.dockerignore` | Trims the build context (excludes `bin/`, `obj/`, `node_modules/`, `src/hqqq-ui/`, `data/`, `.env`, etc.). |

Each service Dockerfile is multi-stage (`mcr.microsoft.com/dotnet/sdk:10.0`
build → `mcr.microsoft.com/dotnet/aspnet:10.0` runtime), runs as a
non-root `app` user (uid 10001), and exposes a single port (`8080` for
web services, `8081` for worker management hosts).

### One-line bring-up

```powershell
.\scripts\phase2-up.ps1
.\scripts\bootstrap-kafka-topics.ps1
.\scripts\phase2-smoke.ps1
```

```bash
./scripts/phase2-up.sh
./scripts/bootstrap-kafka-topics.sh
./scripts/phase2-smoke.sh
```

The `phase2-up` script is a thin wrapper around:

```bash
docker compose -f docker-compose.yml -f docker-compose.phase2.yml up -d --build
```

`phase2-smoke.ps1` / `.sh` already honor `HQQQ_GATEWAY_BASE_URL` (default
`http://localhost:5030`) and work unchanged against the containerized
gateway.

### Container DNS, ports, and health

| Service | Container DNS:port | Host port | Health endpoint |
|---------|--------------------|-----------|-----------------|
| `hqqq-reference-data` | `hqqq-reference-data:8080` | `5020` | `GET /healthz/ready` |
| `hqqq-ingress`        | `hqqq-ingress:8081`        | `5081` | `GET /healthz/ready` |
| `hqqq-quote-engine`   | `hqqq-quote-engine:8081`   | `5082` | `GET /healthz/ready` |
| `hqqq-persistence`    | `hqqq-persistence:8081`    | `5083` | `GET /healthz/ready` |
| `hqqq-gateway`        | `hqqq-gateway:8080`        | `5030` | `GET /healthz/ready` |
| `hqqq-analytics` (profile=`analytics`) | `hqqq-analytics:8081` | `5084` | n/a — exit code is the signal |

Worker services run only the management host inside the container and
are configured with `Management__BindAddress=0.0.0.0` so the gateway
aggregator can reach `/healthz/ready` and `/metrics` across the docker
network. The gateway aggregator's downstream URLs are wired in
`docker-compose.phase2.yml`:

```
Gateway__Health__Services__ReferenceData__BaseUrl = http://hqqq-reference-data:8080
Gateway__Health__Services__Ingress__BaseUrl       = http://hqqq-ingress:8081
Gateway__Health__Services__QuoteEngine__BaseUrl   = http://hqqq-quote-engine:8081
Gateway__Health__Services__Persistence__BaseUrl   = http://hqqq-persistence:8081
Gateway__Health__Services__Analytics__BaseUrl     = http://hqqq-analytics:8081
```

### Quote-engine checkpoint volume

`hqqq-quote-engine` writes its crash-recovery checkpoint to
`/data/quote-engine/checkpoint.json`, mapped to the named volume
`quote_engine_data`. The checkpoint **survives container restart and
recreate**. It is removed only when the volume is explicitly dropped:

```powershell
.\scripts\phase2-down.ps1 -RemoveVolumes
```

### Analytics — one-shot job

The analytics container is gated behind the `analytics` profile so it is
**not** started by the default `up`. Run a single report on demand:

```powershell
$env:Analytics__StartUtc = '2026-04-17T00:00:00Z'
$env:Analytics__EndUtc   = '2026-04-18T00:00:00Z'
$env:Analytics__EmitJsonPath = '/artifacts/hqqq-report.json'   # optional
docker compose -f docker-compose.yml -f docker-compose.phase2.yml `
    --profile analytics run --rm hqqq-analytics
```

```bash
Analytics__StartUtc=2026-04-17T00:00:00Z \
Analytics__EndUtc=2026-04-18T00:00:00Z \
docker compose -f docker-compose.yml -f docker-compose.phase2.yml \
    --profile analytics run --rm hqqq-analytics
```

When `Analytics__EmitJsonPath` is set, the container writes the artifact
to `/artifacts` inside the container, which is bind-mounted to
`./artifacts/` in the repo root. Exit codes follow Section 7.

### Image build commands (single image at a time)

For CI or one-off rebuilds outside compose, build from the repo root:

```bash
docker build -f src/services/hqqq-reference-data/Dockerfile -t hqqq-reference-data:dev .
docker build -f src/services/hqqq-ingress/Dockerfile        -t hqqq-ingress:dev        .
docker build -f src/services/hqqq-quote-engine/Dockerfile   -t hqqq-quote-engine:dev   .
docker build -f src/services/hqqq-persistence/Dockerfile    -t hqqq-persistence:dev    .
docker build -f src/services/hqqq-gateway/Dockerfile        -t hqqq-gateway:dev        .
docker build -f src/services/hqqq-analytics/Dockerfile      -t hqqq-analytics:dev      .
```

### Legacy monolith

The Phase 1 `hqqq-api` monolith is intentionally **not** part of the
Phase 2 compose stack. To run it for parity testing, build/run it
independently via `scripts/build-hqqq-api-docker.ps1` and either point
the gateway at it (`Gateway__DataSource=legacy` +
`Gateway__LegacyBaseUrl=http://host.docker.internal:5000`) or run both
on the host.

### What is intentionally deferred to Phase 3

- Kubernetes manifests / Helm charts (Phase 3 work — Phase 2's cloud
  posture is Azure Container Apps, see [azure-deploy.md](azure-deploy.md)).
- HA Kafka / Redis / Timescale topologies.
- Image signing, SBOMs, vulnerability scans in CI.
- Distroless / chiselled .NET base images.

(Azure assets — ACR push, Bicep, Container Apps deploy — are now
delivered in D4 under `infra/azure/` and the
`phase2-images.yml` / `phase2-deploy.yml` workflows; multi-replica
gateway smoke is covered by D5 — see Section 12.)

## 12. Multi-gateway replica smoke (Phase 2D5)

Phase 2D5 adds a compose-based replica-smoke topology that proves two
gateway instances can correctly fan out the same live SignalR stream
when both subscribe to the shared Redis pub/sub channel. The point of
D5 is gateway-replica correctness, not full HA: only the gateway is
duplicated; quote-engine, persistence, ingress, reference-data, and
analytics stay single-instance.

### Layout

| File | Role |
|------|------|
| `docker-compose.yml` | Infra base. |
| `docker-compose.phase2.yml` | App-tier overlay (gateway-a on host port 5030). |
| `docker-compose.replica-smoke.yml` | Adds `hqqq-gateway-b` on host port 5031, sharing the same Redis. |
| `tests/Hqqq.Gateway.ReplicaSmoke/` | Console exe smoke harness. |

### Endpoints

| Replica | Compose service | Container DNS:port | Host port | Health |
|---------|-----------------|--------------------|-----------|--------|
| gateway-a | `hqqq-gateway`   | `hqqq-gateway:8080`   | `5030` | `GET /healthz/ready` |
| gateway-b | `hqqq-gateway-b` | `hqqq-gateway-b:8080` | `5031` | `GET /healthz/ready` |

### Bring-up

```powershell
.\scripts\replica-smoke-up.ps1
.\scripts\bootstrap-kafka-topics.ps1
.\scripts\replica-smoke.ps1
```

```bash
./scripts/replica-smoke-up.sh
./scripts/bootstrap-kafka-topics.sh
./scripts/replica-smoke.sh
```

`replica-smoke-up.*` is a thin wrapper around the three-file compose
overlay:

```bash
docker compose \
    -f docker-compose.yml \
    -f docker-compose.phase2.yml \
    -f docker-compose.replica-smoke.yml up -d --build
```

### What the harness verifies

`scripts/replica-smoke.*` invokes the `Hqqq.Gateway.ReplicaSmoke` console
exe, which:

1. probes `GET /healthz/ready` and `GET /api/quote` on both gateways,
   requiring `2xx` from each;
2. opens two SignalR clients — one to `5030/hubs/market`, one to
   `5031/hubs/market` — and registers a `QuoteUpdate` handler on each;
3. publishes a single deterministic `QuoteUpdateEnvelope` to
   `hqqq:channel:quote-update` via Redis pub/sub. The harness asserts
   that the `PUBLISH` was received by at least 2 subscribers (one per
   gateway replica);
4. waits for both SignalR clients to receive the same `QuoteUpdate`
   within the configured timeout (default 15s) and checks that the
   `Nav`, `QuoteState`, `AsOf`, and `IsLive` fields round-trip
   unchanged.

Exit code `0` is the only "PASS"; everything else exits `1`.

### Environment overrides

| Variable | Default | Purpose |
|----------|---------|---------|
| `HQQQ_GATEWAY_A_BASE_URL` | `http://localhost:5030` | gateway-a base URL for REST + SignalR. |
| `HQQQ_GATEWAY_B_BASE_URL` | `http://localhost:5031` | gateway-b base URL for REST + SignalR. |
| `Redis__Configuration` | `localhost:6379` | StackExchange.Redis configuration string used by the harness for the pub/sub publish. |
| `Gateway__BasketId` | `HQQQ` | Basket id placed in the synthetic envelope. Both replicas must agree on this. |
| `HQQQ_REPLICA_SMOKE_TIMEOUT_SECONDS` | `15` | Per-step timeout for SignalR connect and message wait. |

### Tear-down

```powershell
docker compose `
    -f docker-compose.yml `
    -f docker-compose.phase2.yml `
    -f docker-compose.replica-smoke.yml down
```

```bash
docker compose \
    -f docker-compose.yml \
    -f docker-compose.phase2.yml \
    -f docker-compose.replica-smoke.yml down
```

Add `-v` to also drop named volumes (Timescale, Redis, quote-engine
checkpoint, Prometheus/Grafana state).

### Scale-out assumptions documented by D5

- Both replicas resolve the same Redis instance and the same
  `Gateway__BasketId`. This is what allows them to share snapshot keys
  and the live pub/sub channel.
- Sticky sessions are NOT required for `/hubs/market` — every replica
  receives every published `QuoteUpdate` and broadcasts to its own
  clients independently.
- The contract surface (REST routes, SignalR hub path, hub event name,
  DTO shapes) is unchanged by D5.

### Deferred beyond D5

- HA Kafka / Redis / Timescale topologies.
- Multi-instance quote-engine / persistence / ingress / reference-data.
- Kubernetes manifests / Helm charts.
- SignalR Redis backplane (only relevant if a future scale-out moves
  off "every replica receives every update").
- Cross-region or multi-AZ topology.

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
