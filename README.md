# HQQQ — Nasdaq-100 ETF Engine

**Frontend Live Demo:** <https://delightful-dune-08a7a390f.1.azurestaticapps.net/>  

Backend Live Demo: <https://app-hqqq-api-mvp-cdgffghwf8c4hgdh.eastus-01.azurewebsites.net/api/system/health>  

---

HQQQ is a synthetic Nasdaq-100 ETF pricing engine for real-time iNAV calculation & monitoring.
It ingests constituent prices, applies a hybrid basket model, and streams live
quote updates to a terminal-style frontend.

$$iNAV_t = \text{ScaleFactor} \times \sum_{i}(P_{i,t} \times Q_i)$$

In plain English: use live constituent prices (`P`) and calibrated position
sizes (`Q`) to compute a raw basket value, then apply a continuity-preserving
`ScaleFactor` so the displayed iNAV stays stable across basket transitions.

## What this MVP delivers

```text
Basket sources (Stock Analysis / Schwab / Alpha Vantage / Nasdaq API)
  -> Hybrid basket builder
       -> anchor + tail merge + weight normalization
       -> merged-basket SHA-256 fingerprint (idempotent refresh)
       -> active/pending semantics
            -> refresh creates pending basket
            -> market-open activation + continuity-preserving recalibration
       -> corporate-action adjustment (post-AsOf split -> scaled disclosed shares)
       -> pricing basis + scale-state persistence (restore on restart)
       -> quote-engine basket inputs

Tiingo WebSocket (IEX quotes)
  -> realtime ingestion (auto reconnect)
  -> latest-price store
       -> stale detection (>5s flagged stale)
       -> 2-second REST fallback when websocket unavailable
       -> quote-engine price inputs

quote-engine
  -> compute iNAV + qqq proxy + premium/discount + freshness + movers
  -> REST/SignalR APIs
       |-> Frontend Market page (live quote + charts + feed freshness)
       |-> Frontend Constituents page (holdings + concentration + quality)
       |-> Frontend History page (/api/history range analytics)
       \-> Frontend System page (health + runtime/dependency metrics)
```

| Capability | Status |
|---|---|
| **Hybrid basket construction** — anchor (Stock Analysis top-25 or Schwab top-20) + tail (Alpha Vantage filtered or Nasdaq proxy), merged with weight normalization | Live |
| **Active / pending basket semantics** — refresh creates pending basket, activation occurs at market open with continuity-preserving recalibration | Live |
| **Tiingo WebSocket streaming** — real-time IEX quotes with automatic reconnect | Live |
| **2-second REST fallback** — polling fallback when WebSocket is unavailable | Live |
| **Stale detection** — prices older than 5 seconds are marked stale and surfaced to UI metrics | Live |
| **Scale-state persistence** — scale factor and basis state restored after restart | Live |
| **Merged-basket fingerprinting** — SHA-256 fingerprint enables idempotent refresh behavior | Live |
| **Raw-source caching** — upstream source payloads cached to `data/raw/` for fetch resilience | Live |
| **Corporate-action adjustment** — splits after basket `AsOfDate` scale disclosed shares before pricing-basis build | Live |
| **Frontend Market page** — live iNAV, QQQ proxy price, premium/discount, charts, movers, feed freshness | Live |
| **Frontend Constituents page** — holdings table, concentration metrics, quality stats | Live |
| **Frontend History page** — range-based historical analytics from backend `/api/history` | Live |
| **Frontend System page** — health cards, runtime metrics, dependency status | Live |

## Repository structure (MVP)

```text
nasdaq-etf-engine/
├── README.md
├── .env.example
├── docs/
│   ├── architecture.md
│   └── runbook.md
├── src/
│   ├── hqqq-api/                       # ASP.NET Core 10 backend (modular monolith)
│   │   ├── Configuration/              # Options + env-var mapping
│   │   ├── Hubs/                       # SignalR MarketHub
│   │   ├── Modules/
│   │   │   ├── Basket/                 # Hybrid basket construction + caching
│   │   │   ├── CorporateActions/       # Split adjustment (basket snapshot → pricing basis)
│   │   │   ├── MarketData/             # Tiingo WS/REST ingestion + latest prices
│   │   │   ├── Pricing/                # iNAV engine + broadcast + scale state
│   │   │   ├── History/                # /api/history range query + stats
│   │   │   ├── Benchmark/              # Record/replay benchmark support
│   │   │   └── System/                 # Health + metrics
│   │   └── Program.cs
│   ├── hqqq-api.tests/                 # xUnit tests
│   └── hqqq-ui/                        # React + Vite frontend
│       └── src/
│           ├── app/                    # Router
│           ├── components/             # Reusable UI primitives
│           ├── layout/                 # App shell
│           ├── lib/                    # API, hooks, adapters, types
│           ├── pages/                  # Market/Constituents/History/System
│           └── styles/                 # Tailwind theme tokens
└── docker-compose.yml
```

## Repository structure (Phase 2)

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
│       ├── release-checklist.md          # Release gate (D6)
│       ├── rollback.md                   # Rollback playbook (D6)
│       ├── config-matrix.md              # Per-service config surface (D6)
│       ├── topics.md
│       └── redis-keys.md
├── infra/
│   ├── azure/                            # Bicep + GitHub OIDC for Azure Container Apps (D4)
│   │   ├── main.bicep
│   │   ├── modules/
│   │   ├── params/
│   │   └── README.md
│   └── prometheus/
├── docker-compose.yml                    # Infra base: Timescale, Redis, Kafka, Kafka UI, Prometheus, Grafana
├── docker-compose.phase2.yml             # Phase 2 app-tier overlay (D3)
├── docker-compose.replica-smoke.yml      # Multi-gateway replica-smoke overlay (D5)
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
│   │   ├── hqqq-reference-data/          # Basket + corporate-action service
│   │   ├── hqqq-ingress/                 # Tiingo ingest worker (stub today)
│   │   ├── hqqq-quote-engine/            # iNAV compute + Redis pub/sub publisher
│   │   ├── hqqq-gateway/                 # REST + SignalR serving gateway (D1 native health, D2 live fan-out)
│   │   ├── hqqq-persistence/             # TimescaleDB writer worker
│   │   └── hqqq-analytics/               # One-shot Timescale report job
│   ├── tools/
│   │   └── hqqq-bench/                   # Offline replay + benchmark CLI
│   ├── hqqq-api/                         # [Legacy] Phase 1 modular monolith
│   ├── hqqq-api.tests/                   # [Legacy] Phase 1 tests
│   └── hqqq-ui/                          # React + Vite frontend
│       └── src/
│           ├── app/                      # Router
│           ├── components/               # Reusable UI primitives
│           ├── layout/                   # App shell
│           ├── lib/                      # API, hooks, adapters, types
│           ├── pages/                    # Market/Constituents/History/System
│           └── styles/                   # Tailwind theme tokens
└── tests/
    ├── Hqqq.Contracts.Tests/
    ├── Hqqq.ReferenceData.Tests/
    ├── Hqqq.Ingress.Tests/
    ├── Hqqq.QuoteEngine.Tests/
    ├── Hqqq.Gateway.Tests/
    ├── Hqqq.Gateway.ReplicaSmoke/        # D5 multi-gateway smoke harness
    └── Hqqq.Persistence.Tests/
```

Deep architecture details are documented in [docs/architecture.md](docs/architecture.md).
Phase 2 migration status is tracked in [docs/phase2/restructure-notes.md](docs/phase2/restructure-notes.md).

## Phase 2 — event-driven serving layer (in place through D6)

Phase 2 splits ingestion, compute, persistence, and gateway concerns
into narrow services connected by Kafka, Redis, and Timescale. The
short version is below; `docs/architecture.md` keeps the full version
with the same component names and flow.

```text
Tiingo WebSocket
  -> ingress-service (normalize ticks + add metadata)             [stub today; legacy monolith ingests]
  -> Kafka topic: market.raw_ticks.v1 (key=symbol)
       |-> hqqq-quote-engine (consumer group A)
       |     -> compute basket/iNAV
       |     -> Redis latest views: hqqq:snapshot / constituents / freshness
       |     -> Redis pub/sub: hqqq:channel:quote-update            (D2 live fan-out)
       |     -> Kafka: pricing.snapshots.v1
       |
       |-> hqqq-persistence (consumer group B)
       |     -> TimescaleDB hypertables (quote_snapshots, raw_ticks)
       |     -> 1m/5m continuous aggregates + retention
       |
       \-> hqqq-analytics (one-shot job, reads Timescale only)

  -> hqqq-gateway (REST + SignalR; 1..N replicas, multi-replica-safe)
       |-> /api/quote, /api/constituents      (Redis snapshots)
       |-> /api/history?range=                (Timescale)
       |-> /api/system/health                  (D1 native aggregator)
       \-> /hubs/market SignalR fan-out        (subscribes to hqqq:channel:quote-update;
                                                broadcasts locally; no SignalR Redis backplane)
```

D-phase status (in place):

| Slice | What it delivers |
|-------|------------------|
| **D1** | Gateway-native `/api/system/health` aggregator (default) |
| **D2** | Live `QuoteUpdate` fan-out via Redis pub/sub `hqqq:channel:quote-update` (multi-replica-safe by construction) |
| **D3** | Containerized Phase 2 app tier (`docker-compose.phase2.yml` + Dockerfiles + wrapper scripts) |
| **D4** | Azure Container Apps deployment assets (`infra/azure/` Bicep + `phase2-images.yml` + `phase2-deploy.yml` GitHub OIDC) |
| **D5** | Multi-gateway replica-smoke topology + harness (`docker-compose.replica-smoke.yml`, `tests/Hqqq.Gateway.ReplicaSmoke/`) |
| **D6** | Operator docs closeout — refreshed architecture / runbook / Phase 2 docs + new release-checklist, rollback, config-matrix |

Design principle:
- Kafka is the durable event log and fan-out backbone for compute and persistence.
- Redis is the latest-state serving layer **and** the live `/hubs/market` fan-out channel (per-replica subscribe + local broadcast — no SignalR Redis backplane).

### Phase 3 — Kubernetes for app-tier operations (deferred)

Planned scope (NOT in Phase 2):
- Run gateway, ingress, and workers on Kubernetes (`Deployment` + `Service`)
- Use HPA for gateway elasticity (CPU and/or custom metrics)
- Manage runtime config via `ConfigMap` and secrets via `Secret`
- HA topologies for Kafka / Redis / Timescale themselves; multi-instance quote-engine / persistence / ingress / reference-data
- Keep stateful infra (Kafka/Redis/Postgres) independent or managed where appropriate

Operating principle: stateless app tier on Kubernetes for operability and
elasticity; stateful infra treated as separate reliability concerns.

Full phase design and component diagrams are in [docs/architecture.md](docs/architecture.md).

## MVP dependencies

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 10.0+ | `dotnet --version` |
| Node.js | 22 LTS | Pinned in `src/hqqq-ui/.nvmrc` |
| npm | 10.x | Bundled with Node 22 |

## API endpoints

| Method | Path | Description |
|---|---|---|
| GET | `/api/quote` | Current iNAV quote snapshot |
| GET | `/api/constituents` | Holdings with prices, weights, and quality metrics |
| GET | `/api/basket/current` | Active/pending basket state and fingerprints |
| POST | `/api/basket/refresh` | Force basket re-fetch and merge |
| GET | `/api/marketdata/status` | Ingestion health, coverage, WebSocket/fallback state |
| GET | `/api/marketdata/latest` | Latest prices (all or filtered symbols) |
| GET | `/api/system/health` | Service/runtime/dependency health snapshot |
| GET | `/api/history?range=` | Historical quote analytics (`1D/5D/1M/3M/YTD/1Y`) |
| GET | `/metrics` | Prometheus-compatible metrics |
| WS | `/hubs/market` | SignalR market stream (`QuoteUpdate`) |

Swagger (local): <http://localhost:5015/swagger>

### Quote delivery model

| Channel | Payload | Usage |
|---|---|---|
| `GET /api/quote` | Full `QuoteSnapshot` (includes full `series`) | Initial load / reconnect resync |
| SignalR `QuoteUpdate` | Slim realtime delta (no full `series`) | Low-bandwidth continuous updates |

## Frontend pages

| Route | Page | Data source |
|---|---|---|
| `/market` | Market | SignalR `QuoteUpdate` + REST `/api/quote` |
| `/constituents` | Constituents | REST `/api/constituents` polling |
| `/history` | History | REST `/api/history?range=` |
| `/system` | System | REST `/api/system/health` polling |

`/` redirects to `/market`.

## Run and validation guide

All setup, startup, deployment command snippets, and smoke-test procedures are
consolidated in [docs/runbook.md](docs/runbook.md).

Phase 2 operator entry points:

- [docs/phase2/local-dev.md](docs/phase2/local-dev.md) — host-`dotnet run` and containerized (D3) walkthrough
- [docs/phase2/azure-deploy.md](docs/phase2/azure-deploy.md) — Azure Container Apps deployment (D4)
- [docs/phase2/release-checklist.md](docs/phase2/release-checklist.md) — pre/post-deploy gate (D6)
- [docs/phase2/rollback.md](docs/phase2/rollback.md) — rollback playbook (D6)
- [docs/phase2/config-matrix.md](docs/phase2/config-matrix.md) — per-service config surface (D6)

## Known limitations (MVP)

1. **Hybrid basket, not official full-holdings reconstruction.**  
   Basket composition comes from public scraped sources (Stock Analysis, Schwab,
   Alpha Vantage, Nasdaq API) and is not an authorized issuer feed.

2. **`marketPrice` is a QQQ proxy, not a real HQQQ traded price.**  
   HQQQ is synthetic/educational and does not trade on an exchange.
   Premium/discount is therefore computed versus live QQQ as a reference.

## Screenshots

### Market — Real-time iNAV command center
![Market page](images/hqqq-ui-market-demo.png)

### Constituents — Holdings table and basket insights
![Constituents page](images/hqqq-ui-constituents-demo.png)

### History — Historical analytics and tracking error
![History page](images/hqqq-ui-history-demo.png)

### System — Service health and pipeline monitoring
![System page](images/hqqq-ui-system-demo.png)