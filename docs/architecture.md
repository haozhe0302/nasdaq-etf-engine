# Architecture

## 1) System overview

HQQQ is currently running in a **transitional state** between the Phase 1
modular monolith and the Phase 2 service split. Both code paths exist in the
repo and both are compilable and runnable.

Two deployable "faces" coexist today:

- **Phase 1 monolith** — still the reference system and the only path with
  real Tiingo ingestion, basket refresh, corp-action adjustment, and
  `/api/system/health` aggregation.
  - `hqqq-api` (ASP.NET Core 10): basket construction, market-data
    ingestion, iNAV pricing, history aggregation, benchmark endpoints, and
    health/metrics.
  - `hqqq-ui` (React + Vite): live market dashboard, constituents, history,
    and system view.
- **Phase 2 services** — real runtime today for the hot-path serving,
  compute, persistence, and offline reporting responsibilities:
  - `hqqq-reference-data` (web) — basket registry, in-memory seed today.
  - `hqqq-ingress` (worker) — **stub**; Tiingo ingestion still lives in the
    monolith.
  - `hqqq-quote-engine` (worker) — real Kafka consumer + iNAV compute;
    writes Redis snapshots, publishes `pricing.snapshots.v1`, and
    publishes live `QuoteUpdate` envelopes to the Redis pub/sub channel
    `hqqq:channel:quote-update`.
  - `hqqq-persistence` (worker) — real Kafka → TimescaleDB writer for
    `pricing.snapshots.v1` and `market.raw_ticks.v1`; bootstraps
    hypertables, continuous aggregates, and retention policies.
  - `hqqq-gateway` (web) — REST + SignalR serving edge; reads Redis for
    quote/constituents and Timescale for history; native
    `/api/system/health` aggregator (D1); subscribes to
    `hqqq:channel:quote-update` and broadcasts each `QuoteUpdate`
    locally to its connected SignalR clients (D2 / D5
    multi-replica-safe).
  - `hqqq-analytics` (worker) — one-shot report over Timescale.

The architecture intentionally favors explicit module / service boundaries
so responsibilities can migrate out of the monolith in narrow, verifiable
slices rather than in a single big-bang rewrite.

---

## 2) Current backend architecture (`src/hqqq-api`)

### 2.1 Module map

```text
src/hqqq-api/
├── Modules/
│   ├── Basket/        # Hybrid basket construction + refresh + source caching
│   ├── CorporateActions/  # Split adjustment between basket snapshot and pricing basis
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

- `CorporateActions`
  - Adjusts disclosed share counts for stock splits between basket `AsOfDate` and pricing date
  - Implements `ICorporateActionAdjustmentService` and pluggable `ICorporateActionProvider` (Tiingo-backed)
  - Does not mutate active/pending baskets held by `Basket`; produces adjusted clones for pricing only

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
Pricing -> CorporateActions -> (Tiingo daily splits via ICorporateActionProvider)
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

### 2.5 Corporate-action adjustment layer

`CorporateActionAdjustmentService` sits between basket retrieval and pricing-basis
construction. It compensates for share-count staleness when a stock split occurs
between the basket's `AsOfDate` and the pricing runtime date.

```text
BasketSnapshotProvider          CorporateActionAdjustmentService      BasketPricingBasisBuilder
  (active/pending basket)  ──►  (adjust shares for splits)        ──►  (quantity vector)
                                        │
                               ICorporateActionProvider
                               (TiingoCorporateActionProvider)
```

**Data flow**

1. `PricingEngine.TryBootstrapAsync` (and `TryActivatePendingAsync`) obtains the
   active/pending `BasketSnapshot` from `IBasketSnapshotProvider`.
2. It passes the snapshot to `ICorporateActionAdjustmentService.AdjustAsync(snapshot)`.
3. The service queries `ICorporateActionProvider.GetSplitsAsync(symbols, fromDate, toDate)`.
4. For each constituent with official shares and an applicable split, the service
   creates a clone with `SharesHeld *= cumulativeFactor` and appends `:split-adjusted`
   to `SharesSource`.
5. The adjusted snapshot (same fingerprint, new constituent list) is passed to
   `BasketPricingBasisBuilder.Build(adjustedSnapshot, prices)`.
6. The builder propagates the `SharesOrigin` as `"official:split-adjusted"` when
   the `SharesSource` contains `"split-adjusted"`.

**Why original snapshots remain immutable**

Active/pending basket semantics rely on fingerprint-based idempotency. The adjustment
layer produces a *clone* of the snapshot with modified constituent records; the original
objects held by `BasketSnapshotProvider` are never mutated. The adjusted snapshot
retains the original fingerprint so that scale-state persistence and basket-activation
checks remain consistent.

**Cache and failure behavior**

- **`TiingoCorporateActionProvider`**: per-symbol in-memory cache with 1-hour TTL;
  concurrency-limited to 5 simultaneous Tiingo requests; per-symbol failures are
  isolated (one failing ticker does not affect others).
- **`CorporateActionAdjustmentService`**: caches the most recent `AdjustedBasketResult`
  keyed by `(basket.Fingerprint, runtimeDate)`. On provider failure the service
  returns the unadjusted snapshot and sets `Report.ProviderFailed = true`
  (visible in `/api/system/health` under the `corporate-actions` dependency).

**Provenance model**

| Field | Description |
|-------|-------------|
| `BasketConstituent.SharesSource` | Appended with `:split-adjusted` when the shares were multiplied by a split factor |
| `PricingBasisEntry.SharesOrigin` | `"official:split-adjusted"` for adjusted entries, unchanged `"official"` or `"derived"` otherwise |
| `AdjustmentReport.Adjustments[]` | Per-symbol audit: original shares, adjusted shares, cumulative factor, individual split events |
| `AdjustmentReport.ProviderFailed` | Whether the split-data provider threw; if `true`, shares are unadjusted and the system operates in degraded mode |

**Adding future corporate-action types**

1. Define a new event record alongside `SplitEvent` (e.g. `SpinOffEvent`).
2. Extend `ICorporateActionProvider` with a new query method (e.g. `GetSpinOffsAsync`).
3. Add a new adjustment pass inside `CorporateActionAdjustmentService.ComputeAdjustmentAsync`
   after the split pass.
4. The `AdjustmentReport` and `ConstituentAdjustment` records can be extended with
   additional action-type-specific fields.

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
| `/constituents` | `ConstituentsPage` | REST `/api/constituents` polling (3s) |
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

## 4) Current transitional architecture (Phase 2 through D5)

> **See Phase 2 repo restructure details here [phase2/restructure-notes.md](phase2/restructure-notes.md)**
>
> Operator references: [phase2/local-dev.md](phase2/local-dev.md),
> [phase2/azure-deploy.md](phase2/azure-deploy.md),
> [phase2/release-checklist.md](phase2/release-checklist.md),
> [phase2/rollback.md](phase2/rollback.md),
> [phase2/config-matrix.md](phase2/config-matrix.md).

Phase 2 splits serving, compute, persistence, and offline analytics out of
the monolith into narrow services connected by Kafka, Redis, and Timescale.
The split is **not** all-or-nothing — each responsibility migrates when a
cutover slice is proven stable.

### 4.1 Responsibility split (today)

| Responsibility | Owner today | Notes |
|---|---|---|
| Tiingo WS / REST ingestion | `hqqq-api` (monolith) | `hqqq-ingress` is a stub host only |
| Basket refresh + corp-action adjustment | `hqqq-api` (monolith) | `hqqq-reference-data` only serves an in-memory seed basket |
| iNAV compute + Redis materialization | `hqqq-quote-engine` | Real Kafka consumers, writes `hqqq:snapshot:{basketId}` + `hqqq:constituents:{basketId}`, publishes `pricing.snapshots.v1`, publishes live `QuoteUpdate` envelopes to Redis pub/sub `hqqq:channel:quote-update` |
| `pricing.snapshots.v1` + `market.raw_ticks.v1` → Timescale | `hqqq-persistence` | Bootstraps hypertables, `quote_snapshots_1m`/`quote_snapshots_5m` continuous aggregates, and retention policies |
| Latest-state serving (`/api/quote`, `/api/constituents`) | `hqqq-gateway` (Redis) | Per-endpoint `Gateway:Sources:Quote=redis` / `Gateway:Sources:Constituents=redis` |
| History serving (`/api/history?range=`) | `hqqq-gateway` (Timescale) | `Gateway:Sources:History=timescale`; reads `quote_snapshots` directly |
| System-health aggregation (`/api/system/health`) | `hqqq-gateway` (native aggregator, D1) | Default `Gateway:Sources:SystemHealth=aggregated`; `legacy` (forwards to monolith) and `stub` remain available for cutover/offline |
| Realtime SignalR fan-out (`/hubs/market`) | `hqqq-gateway` (multi-replica safe, D2 + D5) | Every replica subscribes independently to Redis pub/sub `hqqq:channel:quote-update` and broadcasts locally; SignalR Redis backplane deliberately not used |
| Offline reporting over history | `hqqq-analytics` | One-shot `Analytics:Mode=report` job reads Timescale; replay/anomaly/backfill deferred |

### 4.2 Data plane

```text
Tiingo WebSocket
      |
      v
[hqqq-api (legacy ingestion)]  --publishes-->  market.raw_ticks.v1
                                (today)            |
                                                   v
                                   +======== Kafka =========+
                                   | market.raw_ticks.v1    |  key = symbol    (3 partitions)
                                   | market.latest_by_symbol.v1 (3, compact)   |
                                   | refdata.basket.active.v1 (1, compact)     |
                                   | pricing.snapshots.v1    (1, produced by   |
                                   |                          quote-engine)    |
                                   +=========================+
                                      |                 |
                          consumer group A        consumer group B
                                      |                 |
                                      v                 v
                           [hqqq-quote-engine]  [hqqq-persistence]
                           - iNAV compute       - batched writes
                           - Redis snapshots    - quote_snapshots
                           - publish            - raw_ticks
                             pricing.snapshots  - 1m / 5m CAGGs
                                 |              - retention policies
                                 v
                           +===== Redis =====+
                           | hqqq:snapshot:{basketId}    |   <-- snapshot keys (R/W)
                           | hqqq:constituents:{basketId}|
                           | hqqq:basket:active:{basketId}|
                           | hqqq:freshness:{basketId}   |
                           | hqqq:channel:quote-update    |   <-- live pub/sub channel (D2)
                           +==============================+
                                 |                   |
                       (snapshot reads)        (pub/sub fan-out)
                                 |                   |
                                 v                   v
                           [hqqq-gateway] (1..N replicas, D5)
                             |        |        \
                             |        |         +-- /hubs/market SignalR fan-out
                             |        |             (per-replica subscribe + local broadcast;
                             |        |              no SignalR Redis backplane)
                             |        +--- /api/history ----> TimescaleDB (quote_snapshots)
                             |        +--- /api/system/health (D1 native aggregator scrapes
                             |                                 each Phase 2 worker's
                             |                                 /healthz/ready + Redis/Timescale)
                             |
                           REST /api/quote, /api/constituents (Redis)

                           [hqqq-analytics]    (one-shot, Timescale only)
```

### 4.3 Why Kafka + Redis + Timescale

- **Kafka** is the durable event log and fan-out backbone. Raw provider
  ticks and derived pricing snapshots both land here before being
  consumed by independent groups.
- **Redis** is the latest-state serving layer for low-latency read paths
  on the gateway. It is a cache: if it goes empty, the quote-engine
  re-populates on the next compute cycle.
- **TimescaleDB** is the history + analytics store. It is the only source
  the gateway reads for `/api/history` in C2 mode, and the only source
  `hqqq-analytics` reads in C4.

### 4.4 What still lives in the monolith

- Real Tiingo WebSocket / REST ingestion.
- Basket refresh from external issuer feeds and corporate-action
  adjustment.
- Recorder / benchmark tooling (`Benchmark` module).

These remain single-process today and are the next candidates for
extraction. `/api/system/health` aggregation has already moved to the
gateway (D1 native aggregator); the monolith path is now only used as a
`legacy`/`stub` fallback per `Gateway:Sources:SystemHealth`.

### 4.5 D-phase delivered, and what is still deferred

D-phase summary (now in place):

- **D1** — Gateway-native `/api/system/health` aggregator: scrapes
  `/healthz/ready` on each Phase 2 worker plus the local Redis /
  Timescale probes; rolls up to `degraded` instead of crashing when a
  dependency is missing.
- **D2** — Live `QuoteUpdate` fan-out via Redis pub/sub
  (`hqqq:channel:quote-update`): the quote-engine publishes envelopes,
  every gateway replica runs its own subscriber and broadcasts the
  inner DTO to its SignalR clients. SignalR Redis backplane is
  deliberately NOT enabled — the per-replica subscribe + local
  broadcast keeps multi-replica fan-out correct without it.
- **D3** — Containerized Phase 2 app tier: per-service Dockerfiles
  (non-root, pinned `mcr.microsoft.com/dotnet/aspnet:10.0`),
  `docker-compose.phase2.yml` overlay on top of the existing infra
  compose, `phase2-up`/`phase2-smoke`/`phase2-down` wrapper scripts.
- **D4** — Azure Container Apps deployment assets: Bicep under
  `infra/azure/` (ACR + LAW + UAMI + CAE + 5 apps + 1 Manual job),
  GitHub OIDC workflows
  ([phase2-images.yml](../.github/workflows/phase2-images.yml),
  [phase2-deploy.yml](../.github/workflows/phase2-deploy.yml)),
  walkthrough in [phase2/azure-deploy.md](phase2/azure-deploy.md).
- **D5** — Multi-gateway replica smoke: `docker-compose.replica-smoke.yml`
  adds a second gateway replica, `tests/Hqqq.Gateway.ReplicaSmoke/`
  asserts both replicas receive the same `QuoteUpdate` over the shared
  Redis pub/sub channel. Scope is gateway-replica correctness, not full
  HA.
- **D6** — Operator docs closeout: refreshed architecture / runbook /
  Phase 2 docs plus new
  [release-checklist.md](phase2/release-checklist.md),
  [rollback.md](phase2/rollback.md), and
  [config-matrix.md](phase2/config-matrix.md).

Still deferred:

- **Phase 3** — Kubernetes app-tier operationalization (`Deployment` +
  `Service` + HPA for stateless services, managed stateful infra
  Kafka/Redis/Postgres kept separate).
- HA topologies for Kafka / Redis / Timescale themselves (D5 only
  duplicates the gateway).
- Multi-instance quote-engine / persistence / ingress / reference-data
  (singletons today by design).
- Real Tiingo ingestion in `hqqq-ingress`; real issuer-feed and
  corporate-action pipeline in `hqqq-reference-data`; both still served
  by the legacy monolith.
- Replay / anomaly / backfill in `hqqq-analytics`.
- Custom domain + TLS, Azure Files mount for the quote-engine
  checkpoint, scheduled trigger for the analytics job, image signing /
  SBOMs / vulnerability scans in CI.

---

## 5) Current-to-future evolution summary

| Concern | Phase 1 monolith | Phase 2 now (through D5) | Still deferred |
|---|---|---|---|
| Tick transport | In-process ingestion | Monolith still ingests; `hqqq-quote-engine` consumes Kafka ticks produced by the monolith bridge | Real `hqqq-ingress` (Phase 2B remaining) |
| Serving state | In-process memory | Redis (`hqqq:snapshot:*`, `hqqq:constituents:*`); multi-gateway-safe by construction (D5) | HA Redis topology (Phase 3+) |
| History storage | Filesystem snapshots | TimescaleDB hypertables + 1m/5m continuous aggregates + retention policies | Rollup-first history reads (C5+) |
| Gateway read path | Monolith endpoints | `hqqq-gateway` with layered per-endpoint source selection (`redis`/`timescale`/`aggregated`/`legacy`/`stub`) | — |
| System-health | Monolith `/api/system/health` only | `hqqq-gateway` native aggregator (D1, default); `legacy`/`stub` available as fallbacks | — |
| Fan-out model | Single-process internal fan-out | Multi-consumer groups over Kafka **plus** live `hqqq:channel:quote-update` Redis pub/sub; every gateway replica subscribes and broadcasts locally (D2 + D5) | HA Kafka / Redis topologies (Phase 3+) |
| Offline analytics | Ad-hoc / notebook | `hqqq-analytics` one-shot report (`Analytics:Mode=report`); Manual-trigger Container Apps Job in Azure | Replay / anomaly / backfill (C5+ / D) |
| Deployment | Local/prototype style | Local compose (infra base + Phase 2 app-tier overlay D3 + replica-smoke overlay D5) **and** Azure Container Apps via Bicep + GitHub OIDC (D4) | Kubernetes app-tier (Phase 3) |
