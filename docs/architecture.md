# Architecture

## 1) System overview

HQQQ currently runs as a full-stack MVP with two deployable units:

- `hqqq-api` (ASP.NET Core 8): basket construction, market-data ingestion, iNAV
  pricing, history aggregation, benchmark/report endpoints, and health/metrics
- `hqqq-ui` (React + Vite): live market dashboard, constituents, history
  analytics view, and system health view

Current architecture intentionally favors speed of iteration and operational
simplicity (single backend process, local state persistence where needed),
while keeping module boundaries explicit for future service extraction.

---

## 2) Current backend architecture (`src/hqqq-api`)

### 2.1 Module map

```text
src/hqqq-api/
├── Modules/
│   ├── Basket/        # Hybrid basket construction + refresh + source caching
│   ├── MarketData/    # Tiingo WS/REST ingestion + latest price store
│   ├── Pricing/       # iNAV compute, scale calibration, quote broadcast
│   ├── History/       # Range-based historical API over persisted snapshots
│   ├── Benchmark/     # Record/replay report tooling endpoints
│   └── System/        # Health and runtime metrics
├── Hubs/
│   └── MarketHub.cs   # SignalR hub for QuoteUpdate stream
└── Program.cs         # Composition root
```

### 2.2 Responsibilities by module

- `Basket`
  - Builds hybrid ETF basket from Stock Analysis / Schwab / Alpha Vantage / Nasdaq
  - Maintains active and pending basket semantics
  - Produces deterministic basket fingerprints
  - Exposes `/api/basket/current` and `/api/basket/refresh`

- `MarketData`
  - Runs Tiingo WebSocket ingestion
  - Activates REST polling fallback when needed
  - Maintains in-memory latest price store and freshness state
  - Exposes `/api/marketdata/status` and `/api/marketdata/latest`

- `Pricing`
  - Computes real-time quote snapshots from basket + latest prices
  - Preserves iNAV continuity during pending-basket activation (`newScale = oldNAV / newRawValue`)
  - Publishes slim `QuoteUpdate` deltas through SignalR
  - Exposes `/api/quote` and `/api/constituents`

- `History`
  - Serves `/api/history?range=` from persisted snapshot data
  - Performs downsampling, tracking-error stats, and distribution calculations

- `Benchmark`
  - Supports record/replay style benchmark workflows for offline analysis

- `System`
  - Exposes `/api/system/health` with dependency and runtime snapshot
  - Surfaces internal metrics snapshot used by UI/system monitoring

### 2.3 Dependency direction

```text
Pricing -> Basket
Pricing -> MarketData
History -> Pricing persisted outputs (filesystem data products)
System  -> probes all modules for health
Basket / MarketData -> standalone domain providers
```

Design rule: modules consume contracts, not internals; cyclic dependencies are
not allowed.

### 2.4 Delivery model (quote path)

```text
Tiingo WS/REST -> MarketData store -> Pricing engine -> SignalR QuoteUpdate
                                       \-> REST /api/quote (full snapshot)
```

Two-tier payload strategy:
- REST `/api/quote`: full snapshot for bootstrap and reconnect re-sync
- SignalR `QuoteUpdate`: slim incremental updates for lower bandwidth

---

## 3) Current frontend architecture (`src/hqqq-ui`)

### 3.1 Structure

```text
src/hqqq-ui/src/
├── app/          # Router entry
├── components/   # Reusable UI building blocks
├── layout/       # App shell
├── pages/        # Market / Constituents / History / System
├── lib/          # types, api, adapters, hooks, update tracker
└── styles/       # Tailwind theme tokens
```

### 3.2 Route/page model

| Route | Page | Data source |
|---|---|---|
| `/market` | `MarketPage` | REST bootstrap + SignalR `QuoteUpdate` |
| `/constituents` | `ConstituentsPage` | REST `/api/constituents` polling (5s) |
| `/history` | `HistoryPage` | REST `/api/history?range=` polling (30s) |
| `/system` | `SystemPage` | REST `/api/system/health` polling (3s) |

`/` and unknown routes redirect to `/market`.

### 3.3 Frontend data flow contracts

The UI consumes backend data only via hooks in `lib/hooks.ts`:
- `useMarketData()`
- `useConstituentData()`
- `useHistoryData(range)`
- `useSystemData()`
- `useAppStatus()`

Adapters in `lib/adapters.ts` isolate backend DTO shape from UI view models,
so endpoint schema changes are localized.

---

## 4) Future architecture (in progress)

### 4.1 Phase 2: event-driven split

### Goal statement

To support replay, multiple independent consumers, traffic decoupling, and
higher concurrency, the backend evolves from modular monolith internals toward
an event-driven serving architecture:

- ingress writes normalized ticks to Kafka
- quote-engine consumes Kafka and computes iNAV
- persistence-service independently consumes Kafka
- latest snapshots are materialized in Redis
- API/WebSocket gateway serves from Redis
- Prometheus/Grafana tracks lag/stale/latency SLOs

### Component diagram

```text
Tiingo WebSocket
      |
      v
[ingress-service]
  - normalize provider payload
  - attach recv_ts / provider_ts / symbol / seq
  - publish to Kafka topic: market.raw_ticks
      |
      v
==================== Kafka ====================
topic: market.raw_ticks   key = symbol
================================================
      |                         |                         |
      |                         |                         |
      v                         v                         v
[quote-engine]          [persistence-service]     [analytics/replay-worker]
consumer group A        consumer group B          consumer group C
- consume ticks         - persist raw/normalized  - basis stats
- maintain in-memory    - write Postgres/TSDB     - anomaly checks
  latest symbol state   - optional compact table  - backtests/replays
- compute basket/iNAV
- write latest outputs to Redis
- publish snapshot-update event
      |
      v
==================== Redis =====================
keys:
  hqqq:snapshot:latest
  hqqq:constituents:latest
  hqqq:metrics:latest
channels:
  hqqq:snapshot:updates
================================================
      |
      v
[api-gateway / websocket-gateway]
- REST GET /api/quote reads Redis latest snapshot
- client connect sends latest snapshot immediately
- subscribe pub/sub or SignalR backplane
- broadcast updates to 1000+ clients
```

### Why Kafka + Redis split

Kafka is the durable event log and fan-out backbone. Redis is the latest-state
serving layer for low-latency read paths and gateway scale-out. Raw provider
ticks should not bypass Kafka into Redis as the primary pipeline.

### Target capability expansion

- 1000+ concurrent WebSocket clients
- multiple downstream consumers (UI, persistence, alerting, analytics, replay)
- replay/backfill/recalculation as first-class workflows
- multiple ETF baskets (not just HQQQ)
- pluggable multi-provider ingestion
- rolling releases, autoscaling, and better failure isolation

### 4.2 Phase 3: Kubernetes operationalization

Scope and principles:

- Run stateless app tier on Kubernetes: gateway, ingress, worker deployments
- Use `Deployment` + `Service` primitives with HPA-based scaling
- Manage runtime config through `ConfigMap` and sensitive config through `Secret`
- Keep stateful infra (Kafka/Redis/Postgres) independent or managed, based on
  operational maturity and cost profile

Kubernetes is used primarily for operability, elasticity, and availability of
stateless services, not as a hot-path micro-optimization tool.

---

## 5) Current-to-future evolution summary

| Concern | MVP now | Future direction |
|---|---|---|
| Tick transport | Direct ingest in backend process | Kafka as primary durable event bus |
| Serving state | In-process memory + local files | Redis latest-state serving layer |
| History storage | Filesystem snapshots | Postgres/Timescale event/snapshot persistence |
| Fan-out model | Single-process internal fan-out | Multi-consumer groups over Kafka |
| Gateway scale | Single backend instance | Multi-instance API/WS gateway with backplane |
| Deployment | Local/prototype style | Kubernetes app-tier with autoscaling |
