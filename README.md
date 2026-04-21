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

- **Phase 1 (legacy, reference-only)** — the original
  `hqqq-api` modular monolith plus the `hqqq-ui` React app. Retained in
  the repo as reference code; **not required** to run Phase 2.
- **Phase 2 (current service-based app tier, self-sufficient)** — a split
  into `hqqq-gateway`, `hqqq-ingress`, `hqqq-reference-data`,
  `hqqq-quote-engine`, `hqqq-persistence`, and `hqqq-analytics` over
  Kafka / Redis / TimescaleDB. `hqqq-ingress` opens a real Tiingo
  websocket driven by the basket event; `hqqq-reference-data` owns
  the active basket including Phase-2-native corporate-action
  adjustments (splits, renames, transitions, scale-factor continuity).
  Runnable locally via Docker Compose without `hqqq-api`. Deployable to
  Azure Container Apps via GitHub Actions. Multi-replica gateway
  fan-out is real.
- **Phase 3 (under construction)** — Kubernetes app-tier operationalization,
  HA topologies for stateful infra, multi-instance workers.

---

## Architecture at a glance

This diagram is the quick-read view of the current repository state:
Phase 1 and Phase 2 coexist; Phase 2 is **self-sufficient** — the
legacy monolith (`hqqq-api`) is in the repo for reference but does not
participate in Phase 2 runtime.

```text
External market data provider (Tiingo)
  |
  \--> hqqq-ingress (real Tiingo IEX websocket + REST warmup)
         - subscribes to refdata.basket.active.v1 and drives its
           Tiingo subscribe/unsubscribe dynamically off the basket
         - fails fast on missing/placeholder Tiingo__ApiKey

         Kafka topics
           - market.raw_ticks.v1 (key=symbol, ingress -> consumers)
           - market.latest_by_symbol.v1 (compacted, key=symbol)
           - refdata.basket.active.v1 (active basket + AdjustmentSummary)
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
│   │   ├── hqqq-reference-data/          # Active-basket owner. Composite holdings (live File/Http drop → ~100-name fallback seed) + Phase-2-native corp-action adjustment (splits, renames, transitions) → publishes BasketActiveStateV1 with AdjustmentSummary
│   │   ├── hqqq-ingress/                 # Real Tiingo IEX websocket + REST snapshot warmup; publishes market.raw_ticks.v1 + market.latest_by_symbol.v1; subscriptions driven by refdata.basket.active.v1
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
| `hqqq-gateway` | REST + SignalR serving edge. Reads Redis for `/api/quote` and `/api/constituents`, Timescale for `/api/history`. Native `/api/system/health` aggregator: `hqqq-ingress` and `hqqq-reference-data` are architecturally required — any non-healthy status escalates the rollup to `degraded`. Subscribes to `hqqq:channel:quote-update` and broadcasts `QuoteUpdate` locally per replica (no SignalR Redis backplane). |
| `hqqq-ingress` | Real Tiingo IEX websocket consumer with REST snapshot warmup; publishes `market.raw_ticks.v1` (time-series) and `market.latest_by_symbol.v1` (compacted, key=symbol). Subscribes to `refdata.basket.active.v1` and drives Tiingo subscribe/unsubscribe dynamically off the basket. Fails fast at startup if `Tiingo__ApiKey` is missing/placeholder. No stub / hybrid path. |
| `hqqq-quote-engine` | Consumes `market.raw_ticks.v1` + `refdata.basket.active.v1`, runs iNAV compute, writes `hqqq:snapshot:{basketId}` / `hqqq:constituents:{basketId}`, publishes `pricing.snapshots.v1`, and publishes live `QuoteUpdate` envelopes to Redis pub/sub `hqqq:channel:quote-update`. |
| `hqqq-persistence` | Consumes `pricing.snapshots.v1` + `market.raw_ticks.v1` into TimescaleDB hypertables; bootstraps `quote_snapshots_1m` / `quote_snapshots_5m` continuous aggregates and retention policies. |
| `hqqq-analytics` | One-shot Timescale report job (`Analytics:Mode=report`) — not a long-running service. Replay / backfill / anomaly detection are deferred. |
| `hqqq-reference-data` | Composite holdings pipeline (live file/HTTP first, deterministic ~100-name fallback seed otherwise) + **Phase-2-native corporate-action adjustment** (composite file+Tiingo provider; splits, reverse splits, ticker renames, constituent transition detection, scale-factor continuity). Publishes the **full** `BasketActiveStateV1` payload (every constituent + pricing basis + `AdjustmentSummary`) to `refdata.basket.active.v1` on change and on a slow re-publish cadence. `GET /api/basket/current` (with `adjustmentSummary`) and `POST /api/basket/refresh` are real. |

Infrastructure roles:

- **Kafka** — durable event log + fan-out backbone for compute and persistence.
- **Redis** — latest-state serving cache *and* the live `/hubs/market` fan-out trigger channel.
- **TimescaleDB** — historical write side; sole source for `/api/history` and the `hqqq-analytics` report job.

Full Phase 2 architecture, data plane diagram, and the per-endpoint
gateway source-selection matrix are in
[`docs/architecture.md`](docs/architecture.md). Migration status and
explicitly deferred items are in
[`docs/phase2/restructure-notes.md`](docs/phase2/restructure-notes.md).

### Operating mode

Phase 2 has a **single self-sufficient runtime path**. The
`HQQQ_OPERATING_MODE` env (canonical hierarchical key: `OperatingMode`)
is retained as a **logging-posture tag only** for cross-service
consistency in structured logs; it no longer branches any runtime
behaviour. The legacy `hqqq-api` monolith is repo-only reference code
and is never part of the Phase 2 runtime.

Required environment for a Phase 2 bring-up:

| Key | Why |
|-----|-----|
| `Tiingo__ApiKey` | `hqqq-ingress` fails fast without a real Tiingo API key. |
| `Kafka__BootstrapServers` | All services. |
| `Redis__Configuration` | Gateway, quote-engine. |
| `Timescale__ConnectionString` | Persistence, analytics, gateway history. |

`docker-compose.phase2.yml` wires all of the above; `scripts/phase2-up.{ps1,sh}`
bring the stack up without ever starting `hqqq-api`.

### Corporate-action adjustment (Phase-2-native)

`hqqq-reference-data` applies corporate-action adjustments to the basket
**before** publishing. Supported scope (honest):

- forward splits and reverse splits,
- ticker renames / symbol remaps (including chained hops within a window),
- constituent add / remove detection,
- scale-factor continuity across basket transitions.

Out of scope: dividends, spin-offs, mergers, acquisitions, cross-exchange
moves, retroactive re-pricing of stored ticks. The composite provider
reads a deterministic JSON file first (offline-safe, default) and
optionally overlays Tiingo EOD splits when
`ReferenceData__CorporateActions__Tiingo__Enabled=true`. The adjustment
summary is surfaced on `GET /api/basket/current` and on every published
`BasketActiveStateV1` event.

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
| .NET SDK | **pinned in `global.json`** (`10.0.202`, `rollForward=latestFeature`, `allowPrerelease=false`) | `dotnet --version` must print `10.0.202` or a higher `10.0.2xx` stable feature band. RC/preview SDKs (e.g. `10.0.100-rc.*`) will intentionally fail SDK resolution — install the pinned stable SDK rather than loosening `global.json`. CI enforces the same pin via `actions/setup-dotnet@v4` with `global-json-file`. See [docs/phase2/local-dev.md](docs/phase2/local-dev.md#required-net-sdk) for install + verification steps. |
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
