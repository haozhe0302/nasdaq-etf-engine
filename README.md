# HQQQ — Nasdaq-100 ETF Engine

Frontend Live Demo: <https://delightful-dune-08a7a390f.1.azurestaticapps.net/>  
Frontend Local Demo: <http://localhost:5173>

Backend Live Demo: <https://app-hqqq-api-mvp-cdgffghwf8c4hgdh.eastus-01.azurewebsites.net/api/system/health>  
Backend Local Demo: <http://localhost:5015>

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
       -> raw-source caching (data/raw/*)
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
│   ├── hqqq-api/                       # ASP.NET Core 8 backend (modular monolith)
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
└── docker-compose.yml    # Future-phase infra compose file (not required for MVP)
```

Deep architecture details are documented in [docs/architecture.md](docs/architecture.md).

## Future phases (in progress)

### Phase 2 — event-driven serving layer

The design direction is to split ingestion, compute, persistence, and gateway
concerns so replay and multi-consumer workloads become first-class. The diagram
below is the short version; `docs/architecture.md` keeps the full version with
the same component names and flow.

```text
Tiingo WebSocket
  -> ingress-service (normalize ticks + add metadata)
  -> Kafka topic: market.raw_ticks (key=symbol)
       |-> quote-engine (consumer group A)
       |     -> compute basket/iNAV
       |     -> Redis latest views:
       |        hqqq:snapshot / constituents / metrics
       |     -> api-gateway / websocket-gateway
       |        -> REST read + WS broadcast (1000+ clients)
       |
       |-> persistence-service (consumer group B)
       |     -> Postgres/Timescale (history/audit)
       |
       \-> analytics/replay-worker (consumer group C)
             -> anomaly checks / replay / backfill
```

Target outcomes:
- Support 1000+ concurrent WebSocket clients through gateway scale-out
- Support multiple downstream consumers (frontend, persistence, alerting, analytics, replay)
- Support replay / backfill / recalculation workflows
- Support multiple ETF baskets and multiple market-data providers
- Support rolling deploys, autoscaling, and better failure isolation

Design principle:
- Kafka is the durable event log and fan-out backbone
- Redis is the latest-state serving layer (not the primary event pipeline)

### Phase 3 — Kubernetes for app tier operations

Planned scope:
- Run gateway, ingress, and workers on Kubernetes (`Deployment` + `Service`)
- Use HPA for gateway elasticity (CPU and/or custom metrics)
- Manage runtime config via `ConfigMap` and secrets via `Secret`
- Keep stateful infra (Kafka/Redis/Postgres) independent or managed where appropriate

Operating principle: stateless app tier on Kubernetes for operability and
elasticity; stateful infra treated as separate reliability concerns.

Full phase design and component diagrams are in [docs/architecture.md](docs/architecture.md).

## MVP dependencies

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 8.0+ | `dotnet --version` |
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