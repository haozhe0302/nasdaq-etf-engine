# HQQQ — Nasdaq-100 ETF Engine

**Frontend Live Demo:** <https://delightful-dune-08a7a390f.1.azurestaticapps.net/>

Backend Live Demo: <https://app-hqqq-api-mvp-cdgffghwf8c4hgdh.eastus-01.azurewebsites.net/api/system/health>

HQQQ is a synthetic Nasdaq-100 ETF pricing engine for real-time iNAV
calculation and monitoring.

## What HQQQ computes

$$iNAV_t = \text{ScaleFactor} \times \sum_{i}(P_{i,t} \times Q_i)$$

Live constituent prices (`P`) and calibrated position sizes (`Q`)
produce a raw basket value; a continuity-preserving `ScaleFactor`
keeps the displayed iNAV stable across basket transitions.


The repository is in a **transitional architecture**:

- **Phase 1 (legacy)** — the original
  `hqqq-api` modular monolith plus the `hqqq-ui` React app.
- **Phase 2 (current service-based app tier, in-repo)** — a split into
  `hqqq-gateway`, `hqqq-ingress`, `hqqq-quote-engine`,
  `hqqq-persistence`, and `hqqq-analytics` over Kafka / Redis /
  TimescaleDB. Runnable locally via Docker Compose and deployable to
  Azure Container Apps via GitHub Actions rolling images (OIDC) into
  manually-provisioned Container Apps (`rg-hqqq-p2`,
  `ca-hqqq-p2-*`); a legacy Bicep-provisioning path is retained as
  a secondary option. Multi-replica gateway fan-out is real.
- **Phase 3 (under construction)** — Kubernetes app-tier operationalization,
  HA topologies for stateful infra, multi-instance workers.

---

## Architecture at a glance

This diagram is the quick-read view of the current repository state:
Phase 1 and Phase 2 coexist; Phase 2 serving/compute/persistence paths
are real, while some ingestion/reference-data responsibilities remain in
the legacy monolith.

```text
External market data provider (Tiingo)
  |
  +--> Phase 1 reference path (still live for public demo)
  |      src/hqqq-api (monolith)
  |        - real Tiingo WS/REST ingestion
  |        - basket refresh + corp-action adjustment
  |        - can publish market.raw_ticks.v1 bridge events for Phase 2 consumers
  |        - legacy /api/system/health path
  |
  \--> Phase 2 service-based runtime (current app tier in repo)
         (ingestion path depends on HQQQ_OPERATING_MODE:
            hybrid     = monolith bridges ticks; hqqq-ingress + hqqq-reference-data are stubs
            standalone = hqqq-ingress consumes Tiingo IEX directly;
                         hqqq-reference-data publishes a deterministic basket seed)

         Kafka topics
           - market.raw_ticks.v1 (key=symbol, ingress/bridge -> consumers)
           - refdata.basket.active.v1 (active basket state)
           - pricing.snapshots.v1 (quote-engine -> persistence)
                |
                +--> hqqq-quote-engine (consumer group A)
                |      - computes iNAV and quote state
                |      - writes Redis latest views:
                |          hqqq:snapshot:{basketId}
                |          hqqq:constituents:{basketId}
                |          hqqq:freshness:{basketId}
                |      - publishes live QuoteUpdateEnvelope to Redis pub/sub:
                |          hqqq:channel:quote-update
                |      - publishes pricing snapshots to Kafka:
                |          pricing.snapshots.v1
                |
                +--> hqqq-persistence (consumer group B)
                |      - writes TimescaleDB hypertables:
                |          quote_snapshots, raw_ticks
                |      - maintains 1m/5m continuous aggregates + retention
                |
                \--> hqqq-analytics (one-shot job)
                       - reads Timescale only
                       - report mode today; replay/backfill/anomaly deferred

         Redis (latest-state + fan-out trigger)
           - snapshot keys for gateway read path
           - pub/sub channel hqqq:channel:quote-update
                 |
                 v
         hqqq-gateway (REST + SignalR edge, 1..N replicas)
           - /api/quote, /api/constituents      <- Redis snapshots
           - /api/history?range=                <- TimescaleDB
           - /api/system/health                 <- native aggregator
           - /hubs/market                       <- per-replica Redis subscribe +
                                                    local SignalR broadcast
                                                    (no SignalR Redis backplane)
```

For the fully expanded data-plane narrative and mode matrix, see
[`docs/architecture.md`](docs/architecture.md) and
[`src/services/hqqq-gateway/README.md`](src/services/hqqq-gateway/README.md).



---

## Repository structure (current Phase 2 layout)

```text
nasdaq-etf-engine/
├── Hqqq.sln                              # Root solution (all projects)
├── Directory.Build.props                  # Shared .NET build settings
├── docs/
│   ├── architecture.md
│   ├── runbook.md
│   └── phase2/
│       ├── restructure-notes.md          # Migration status + notes
│       ├── local-dev.md                  # Phase 2 operator walkthrough
│       ├── azure-deploy.md               # Azure Container Apps deploy walkthrough
│       ├── release-checklist.md          # Release gate
│       ├── rollback.md                   # Rollback playbook
│       ├── config-matrix.md              # Per-service config surface
│       ├── topics.md
│       └── redis-keys.md
├── infra/
│   ├── azure/                            # Bicep + smoke scripts for the legacy Azure Container Apps provisioning path
│   └── prometheus/
├── docker-compose.yml                    # Infra base: Timescale, Redis, Kafka, Kafka UI, Prometheus, Grafana
├── docker-compose.phase2.yml             # Phase 2 app-tier overlay
├── docker-compose.replica-smoke.yml      # Multi-gateway replica-smoke overlay
├── scripts/
│   ├── bootstrap-kafka-topics.{ps1,sh}
│   ├── phase2-up.{ps1,sh}
│   ├── phase2-down.{ps1,sh}
│   ├── phase2-smoke.{ps1,sh}
│   ├── replica-smoke-up.{ps1,sh}
│   ├── replica-smoke.{ps1,sh}
│   └── build-hqqq-api-docker.ps1
├── src/
│   ├── building-blocks/
│   │   ├── Hqqq.Contracts/               # Cross-service event/DTO contracts
│   │   ├── Hqqq.Domain/                  # Pure domain model (entities, value objects)
│   │   ├── Hqqq.Infrastructure/          # Kafka/Redis/Timescale factories
│   │   └── Hqqq.Observability/           # Metrics, tracing, health builders
│   ├── services/
│   │   ├── hqqq-reference-data/          # Basket service. Hybrid: in-memory seed (monolith owns issuer feeds + corp-actions). Standalone: deterministic repo-controlled basket-seed.json + Kafka publisher
│   │   ├── hqqq-ingress/                 # Tiingo ingest worker. Hybrid: stub. Standalone: real Tiingo IEX websocket + raw-tick + latest-quote Kafka publisher
│   │   ├── hqqq-quote-engine/            # iNAV compute + Redis pub/sub publisher
│   │   ├── hqqq-gateway/                 # REST + SignalR serving gateway
│   │   ├── hqqq-persistence/             # TimescaleDB writer worker
│   │   └── hqqq-analytics/               # One-shot Timescale report job
│   ├── tools/
│   │   └── hqqq-bench/                   # Offline replay + benchmark CLI
│   ├── hqqq-api/                         # [Phase 1 / legacy] modular monolith — still backs the public demo
│   ├── hqqq-api.tests/                   # [Phase 1 / legacy] tests
│   └── hqqq-ui/                          # React + Vite frontend (consumed by both phases)
└── tests/
    ├── Hqqq.Contracts.Tests/
    ├── Hqqq.ReferenceData.Tests/
    ├── Hqqq.Ingress.Tests/
    ├── Hqqq.QuoteEngine.Tests/
    ├── Hqqq.Gateway.Tests/
    ├── Hqqq.Gateway.ReplicaSmoke/        # multi-gateway smoke harness
    └── Hqqq.Persistence.Tests/
```

## What Phase 2 currently includes

Service tier (under `src/services/`):

| Service | Role today |
|---------|------------|
| `hqqq-gateway` | REST + SignalR serving edge. Reads Redis for `/api/quote` and `/api/constituents`, Timescale for `/api/history`. Native `/api/system/health` aggregator (mode-aware: in `standalone`, ingress + reference-data are required dependencies for the rollup). Subscribes to `hqqq:channel:quote-update` and broadcasts `QuoteUpdate` locally per replica (no SignalR Redis backplane). |
| `hqqq-ingress` | **Hybrid (default):** no-op stub; the legacy `hqqq-api` monolith bridges live ticks (an unused `Tiingo__ApiKey` is logged then ignored). **Standalone:** real Tiingo IEX websocket consumer with REST snapshot warmup; publishes `market.raw_ticks.v1` (time-series) and `market.latest_by_symbol.v1` (compacted, key=symbol); fails fast at startup if `Tiingo__ApiKey` is missing/placeholder. |
| `hqqq-quote-engine` | Consumes `market.raw_ticks.v1` + `refdata.basket.active.v1`, runs iNAV compute, writes `hqqq:snapshot:{basketId}` / `hqqq:constituents:{basketId}`, publishes `pricing.snapshots.v1`, and publishes live `QuoteUpdate` envelopes to Redis pub/sub `hqqq:channel:quote-update`. |
| `hqqq-persistence` | Consumes `pricing.snapshots.v1` + `market.raw_ticks.v1` into TimescaleDB hypertables; bootstraps `quote_snapshots_1m` / `quote_snapshots_5m` continuous aggregates and retention policies. |
| `hqqq-analytics` | One-shot Timescale report job (`Analytics:Mode=report`) — not a long-running service. Replay / backfill / anomaly detection are deferred. |
| `hqqq-reference-data` | **Hybrid (default):** in-memory stub; monolith owns issuer feeds + corp-action adjustment + basket activation. **Standalone:** loads a deterministic repo-controlled basket seed (`Resources/basket-seed.json`, ~25 N100 names, override via `ReferenceData__Standalone__SeedPath`) and publishes a fully-materialized `BasketActiveStateV1` to `refdata.basket.active.v1` on startup with a slow re-publish; fails fast on a missing/invalid seed. |

Infrastructure roles:

- **Kafka** — durable event log + fan-out backbone for compute and persistence.
- **Redis** — latest-state serving cache *and* the live `/hubs/market` fan-out trigger channel.
- **TimescaleDB** — historical write side; sole source for `/api/history` and the `hqqq-analytics` report job.

Full Phase 2 architecture, data plane diagram, and the per-endpoint
gateway source-selection matrix are in
[`docs/architecture.md`](docs/architecture.md). Migration status and
explicitly deferred items are in
[`docs/phase2/restructure-notes.md`](docs/phase2/restructure-notes.md).

### Operating modes

Phase 2 runs in one of two operating modes selected by the
`HQQQ_OPERATING_MODE` env var (canonical hierarchical key:
`OperatingMode`). The same value flows into every Container App / compose
service so the entire deployment swaps modes together — see the
hybrid-vs-standalone decision matrix in
[`docs/phase2/azure-deploy.md` §0.2](docs/phase2/azure-deploy.md).

| | `hybrid` (default) | `standalone` |
|---|---|---|
| Live ticks | Bridged from the legacy `hqqq-api` monolith. | `hqqq-ingress` consumes Tiingo IEX directly. |
| Active basket | Bridged from the legacy monolith. | `hqqq-reference-data` publishes a deterministic repo-controlled seed (~25 N100 names). |
| External coupling | Requires the monolith to be running. | Self-contained — no monolith in the path. |
| Required env | (none new) | `Tiingo__ApiKey` on `hqqq-ingress`. |
| Smoke tightness | `phase2-smoke.{ps1,sh}` / `phase2-azure-smoke.sh` assert reachability + 200s. | Same scripts with `-Mode standalone` / `--mode standalone` additionally poll `/api/quote` until `nav > 0` and `/api/constituents` until `holdings` is non-empty. |
| Pick when | Demoing alongside the legacy monolith for parity, or no Tiingo creds available. | Phase 2 must run on its own (interview demo, isolated environment). |

Switching modes is a config-only change — no image rebuild required.
In `docker-compose.phase2.yml` the env var is wired into every Phase 2
service; in Azure the Bicep `operatingMode` parameter (default
`hybrid`) is injected into each Container App's environment.

---

## Operator entrypoint map

| I want to … | Go here |
|-------------|---------|
| Run the legacy Phase 1 reference system locally | [`docs/runbook.md` §§1–10](docs/runbook.md) |
| Run the Phase 2 stack locally on host (`dotnet run`) | [`docs/runbook.md` §11](docs/runbook.md), [`docs/phase2/local-dev.md`](docs/phase2/local-dev.md) |
| Run the containerized Phase 2 app tier | [`docs/runbook.md` §12](docs/runbook.md), `scripts/phase2-up.{ps1,sh}` |
| Run the multi-gateway replica-smoke | [`docs/runbook.md` §13](docs/runbook.md), `scripts/replica-smoke-up.{ps1,sh}` |
| Deploy the Phase 2 app tier to Azure Container Apps | [`docs/phase2/azure-deploy.md`](docs/phase2/azure-deploy.md), [`infra/azure/README.md`](infra/azure/README.md) |
| Walk a release through pre/post-deploy gates | [`docs/phase2/release-checklist.md`](docs/phase2/release-checklist.md) |
| Roll back a Phase 2 release | [`docs/phase2/rollback.md`](docs/phase2/rollback.md) |
| See per-service configuration surface | [`docs/phase2/config-matrix.md`](docs/phase2/config-matrix.md) |
| Read the architecture deep-dive | [`docs/architecture.md`](docs/architecture.md) |
| See Phase 2 migration status / what's deferred | [`docs/phase2/restructure-notes.md`](docs/phase2/restructure-notes.md) |

---

## API contract

| Method | Path | Description |
|---|---|---|
| GET | `/api/quote` | Current iNAV quote snapshot |
| GET | `/api/constituents` | Holdings with prices, weights, and quality metrics |
| GET | `/api/basket/current` | Active/pending basket state and fingerprints (Phase 1 today) |
| POST | `/api/basket/refresh` | Force basket re-fetch and merge (Phase 1 today) |
| GET | `/api/marketdata/status` | Ingestion health, coverage, WebSocket/fallback state (Phase 1 today) |
| GET | `/api/marketdata/latest` | Latest prices (Phase 1 today) |
| GET | `/api/system/health` | Service / runtime / dependency snapshot (Phase 2 gateway: native aggregator; Phase 1: monolith probe) |
| GET | `/api/history?range=` | Historical quote analytics (`1D/5D/1M/3M/YTD/1Y`) |
| GET | `/metrics` | Prometheus-compatible metrics |
| WS | `/hubs/market` | SignalR market stream (`QuoteUpdate`) |

Quote delivery model:

| Channel | Payload | Usage |
|---|---|---|
| `GET /api/quote` | Full `QuoteSnapshot` (includes full `series`) | Initial load / reconnect resync |
| SignalR `QuoteUpdate` | Slim realtime delta (no full `series`) | Low-bandwidth continuous updates |

Frontend pages (`hqqq-ui`):

| Route | Page | Data source |
|---|---|---|
| `/market` | Market | SignalR `QuoteUpdate` + REST `/api/quote` |
| `/constituents` | Constituents | REST `/api/constituents` polling |
| `/history` | History | REST `/api/history?range=` |
| `/system` | System | REST `/api/system/health` polling |

`/` redirects to `/market`.

---

## Toolchain

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 10.0+ | `dotnet --version` |
| Node.js | 22 LTS | Pinned in `src/hqqq-ui/.nvmrc` |
| npm | 10.x | Bundled with Node 22 |
| Docker / Docker Compose | recent | Required for Phase 2 local infra and the Phase 2 app-tier overlay |

---


## Known limitations

1. **Hybrid basket, not official full-holdings reconstruction.**
   Basket composition comes from public scraped sources (Stock
   Analysis, Schwab, Alpha Vantage, Nasdaq API) and is not an
   authorized issuer feed.

2. **`marketPrice` is a QQQ proxy, not a real HQQQ traded price.**
   HQQQ is synthetic / educational and does not trade on an
   exchange. Premium/discount is computed versus live QQQ as a
   reference.

3. **Phase 2 is not an HA platform.** The replica-smoke verifies
   gateway-replica correctness, not full high-availability. Stateful
   infra (Kafka / Redis / Timescale) and non-gateway workers are
   single-instance in the demo environment. This is scheduled to be resolved in future Phase 3.

---

## Phase 3 (under construction)

Phase 3 focuses on Kubernetes operationalization of the **app tier**:
run gateway, ingress, and workers on Kubernetes (`Deployment` + `Service`),
add HPA for gateway elasticity (CPU and/or custom metrics), and manage
runtime config via `ConfigMap` and secrets via `Secret`. Stateful infra
(`Kafka` / `Redis` / `Postgres`) remains independent or managed where
appropriate. Operating principle: stateless app tier on Kubernetes for
operability and elasticity; stateful infra treated as separate reliability
concerns.

## Screenshots

### Market — Real-time iNAV command center
![Market page](images/hqqq-ui-market-demo.png)

### Constituents — Holdings table and basket insights
![Constituents page](images/hqqq-ui-constituents-demo.png)

### History — Historical analytics and tracking error
![History page](images/hqqq-ui-history-demo.png)

### System — Service health and pipeline monitoring
![System page](images/hqqq-ui-system-demo.png)
