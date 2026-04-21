# Architecture

## 1) System overview

HQQQ is currently running in a **transitional state** between the Phase 1
modular monolith and the Phase 2 service split. Both code paths exist in the
repo and both are compilable and runnable.

Two deployable "faces" coexist today:

- **Phase 2 services (current app tier)** — real runtime today for the
  hot-path serving, compute, persistence, and offline reporting
  responsibilities. Runnable locally via Docker Compose and deployable
  to Azure Container Apps.
  - `hqqq-gateway` (web) — REST + SignalR serving edge; reads Redis for
    quote/constituents and Timescale for history; native
    `/api/system/health` aggregator (D1); subscribes to
    `hqqq:channel:quote-update` and broadcasts each `QuoteUpdate`
    locally to its connected SignalR clients (D2 / D5
    multi-replica-safe).
  - `hqqq-quote-engine` (worker) — real Kafka consumer + iNAV compute;
    writes Redis snapshots, publishes `pricing.snapshots.v1`, and
    publishes live `QuoteUpdate` envelopes to the Redis pub/sub channel
    `hqqq:channel:quote-update`.
  - `hqqq-persistence` (worker) — real Kafka → TimescaleDB writer for
    `pricing.snapshots.v1` and `market.raw_ticks.v1`; bootstraps
    hypertables, continuous aggregates, and retention policies.
  - `hqqq-analytics` (worker) — one-shot report over Timescale.
  - `hqqq-reference-data` (web) — **owns the active basket**
    unconditionally. Composite holdings pipeline: config-driven live
    source (`File` or `Http` drop) first, with a committed ~100-name
    deterministic fallback seed (`Resources/basket-seed.json`) as the
    safety net. Runs Phase-2-native **corporate-action adjustment**
    (composite file+Tiingo provider; splits, reverse splits, ticker
    renames, constituent transition detection, scale-factor continuity
    via `Hqqq.Domain.Services.ScaleFactorCalibrator`) **before**
    fingerprinting + publishing. Publishes the **full**
    `BasketActiveStateV1` payload (every constituent + pricing basis +
    `AdjustmentSummary`) to `refdata.basket.active.v1` on change and on
    a slow re-publish cadence. Exposes real
    `GET /api/basket/current` (with `adjustmentSummary`) +
    `POST /api/basket/refresh`.
  - `hqqq-ingress` (worker) — real Tiingo IEX websocket consumer +
    REST snapshot warmup. Publishes `market.raw_ticks.v1` and
    `market.latest_by_symbol.v1` to Kafka. Subscribes to
    `refdata.basket.active.v1` and drives Tiingo subscribe/unsubscribe
    dynamically off the basket event (no static symbol list). Fails
    fast at startup if `Tiingo:ApiKey` is missing/placeholder. **No
    hybrid / stub / log-only path.**

Phase 2 has a single self-sufficient runtime path. `HQQQ_OPERATING_MODE`
(canonical hierarchical key: `OperatingMode`) is retained as a
logging-posture tag only; it no longer branches any runtime behaviour.
The legacy `hqqq-api` monolith is retained in the repo as reference
code and is **not** part of the Phase 2 runtime.
- **Phase 1 monolith (legacy / reference, still present)** — preserved
  and still compilable. Still backs the public live demo links in the
  root `README.md`. It is NOT required for Phase 2 runtime and is not
  a producer on any Phase 2 Kafka topic.
  - `hqqq-api` (ASP.NET Core 10): basket construction, market-data
    ingestion, iNAV pricing, history aggregation, benchmark endpoints, and
    health/metrics.
  - `hqqq-ui` (React + Vite): live market dashboard, constituents, history,
    and system view. The same UI is consumed by both phases.

Role split inside the Phase 2 app tier:

- **Gateway** = serving edge (REST + SignalR + native health aggregator).
- **Quote-engine** = live compute (Kafka consume → iNAV → Redis writes
  + live pub/sub publish).
- **Redis** = latest-state serving cache *and* the live `/hubs/market`
  fan-out trigger channel.
- **Persistence + Timescale** = historical write side (hypertables +
  1m/5m continuous aggregates + retention).
- **Analytics** = one-shot / offline report path over Timescale.

The architecture intentionally favors explicit module / service boundaries
so responsibilities can migrate out of the monolith in narrow, verifiable
slices rather than in a single big-bang rewrite.

---

## 2) Phase 1 monolith backend (legacy / reference, still present)

`src/hqqq-api` is preserved unchanged and still runnable. It is **not**
the current Phase 2 app tier. In the Phase 2 runtime, Tiingo
ingestion, basket refresh, corporate-action adjustment, and
`/api/system/health` aggregation are all owned by Phase 2 services
(`hqqq-ingress`, `hqqq-reference-data`, and `hqqq-gateway` D1
aggregator respectively); the monolith does **not** participate.

The monolith retains its own pre-Phase-2 implementations of those
responsibilities as reference code, and it still backs the public
live demo linked from the root README (the demo Web App is the
monolith on Azure App Service, not the Phase 2 Container Apps
deployment). The module map below describes that legacy
implementation as preserved — it is not a description of the
Phase 2 hot path.

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

## 3) Frontend (`src/hqqq-ui`, consumed by both phases)

The React + Vite frontend is shared. The same `hqqq-ui` is the demo
client today and is the intended client of the Phase 2 gateway as
well; routes and adapters do not change between phases.

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

## 4) Phase 2 service-based runtime (current app tier)

> **See Phase 2 repo restructure details here [phase2/restructure-notes.md](phase2/restructure-notes.md)**
>
> Operator references: [phase2/local-dev.md](phase2/local-dev.md),
> [phase2/azure-deploy.md](phase2/azure-deploy.md),
> [phase2/release-checklist.md](phase2/release-checklist.md),
> [phase2/rollback.md](phase2/rollback.md),
> [phase2/config-matrix.md](phase2/config-matrix.md).

Phase 2 is the **current app tier**: hot-path serving, compute,
persistence, and offline analytics each live in their own service,
connected by Kafka, Redis, and TimescaleDB. Phase 2 is
**self-sufficient at runtime** — Tiingo ingestion (`hqqq-ingress`),
basket refresh + active-basket publication (`hqqq-reference-data`),
and corporate-action adjustment are all owned by Phase 2 services. The
Phase 1 monolith described in §2 is retained in the repo as legacy
reference and is **not** required to run Phase 2. What still lives in
the monolith is reference code only (see §4.4).

Role split (recap from §1):

- **Gateway** = serving edge.
- **Quote-engine** = live compute and live `QuoteUpdate` publisher.
- **Redis** = latest-state cache + live `/hubs/market` fan-out trigger.
- **Persistence + Timescale** = historical write side.
- **Analytics** = one-shot / offline report path.

### 4.1 Responsibility split (today)

`OperatingMode` (`HQQQ_OPERATING_MODE`) is retained only as a logging-
posture tag for cross-service consistency; **no Phase 2 service branches
runtime behaviour on it**. The single self-sufficient runtime path is:

| Responsibility | Phase 2 owner | Notes |
|---|---|---|
| Tiingo WS / REST ingestion | `hqqq-ingress` | Real Tiingo IEX websocket + REST snapshot warmup. Fails fast if `Tiingo__ApiKey` is missing/placeholder. Subscriptions diff off `BasketActiveStateV1.Constituents` mid-session. |
| Basket refresh + active-basket publication | `hqqq-reference-data` | Composite holdings source (live `File`/`Http` drop first, committed ~100-name fallback seed otherwise). Publishes the **full** `BasketActiveStateV1` (every constituent + pricing basis + `AdjustmentSummary`) to `refdata.basket.active.v1` on change and on a slow re-publish cadence. `GET /api/basket/current` + `POST /api/basket/refresh` are real. |
| Corporate-action adjustment | `hqqq-reference-data` | Phase-2-native composite provider (`File` baseline + optional `Tiingo` overlay behind `ReferenceData:CorporateActions:Tiingo:Enabled`). Supported scope: forward / reverse splits, ticker renames (chained), constituent add/remove detection, scale-factor continuity. Out of scope: dividends, spin-offs, mergers. Runs **before** publish; wire summary on the active-basket event. |
| iNAV compute + Redis materialization | `hqqq-quote-engine` | Kafka consumers, writes `hqqq:snapshot:{basketId}` + `hqqq:constituents:{basketId}`, publishes `pricing.snapshots.v1`, publishes live `QuoteUpdate` envelopes to Redis pub/sub `hqqq:channel:quote-update`. |
| `pricing.snapshots.v1` + `market.raw_ticks.v1` → Timescale | `hqqq-persistence` | Bootstraps hypertables, `quote_snapshots_1m`/`quote_snapshots_5m` continuous aggregates, and retention policies. |
| Latest-state serving (`/api/quote`, `/api/constituents`) | `hqqq-gateway` (Redis) | Default `Gateway:Sources:Quote=redis` / `…Constituents=redis`. |
| History serving (`/api/history?range=`) | `hqqq-gateway` (Timescale) | Default `Gateway:Sources:History=timescale`; reads `quote_snapshots` directly. |
| System-health aggregation (`/api/system/health`) | `hqqq-gateway` (native aggregator) | Default `Gateway:Sources:SystemHealth=aggregated`. `hqqq-ingress` and `hqqq-reference-data` are required dependencies — any non-healthy status escalates the rollup. `legacy` / `stub` remain in the codebase as opt-in fallbacks (see §4.5). |
| Realtime SignalR fan-out (`/hubs/market`) | `hqqq-gateway` (multi-replica safe, D2 + D5) | Every replica subscribes independently to Redis pub/sub `hqqq:channel:quote-update` and broadcasts locally; SignalR Redis backplane deliberately not used. |
| Offline reporting over history | `hqqq-analytics` | One-shot `Analytics:Mode=report` job reads Timescale; replay/anomaly/backfill deferred. |

### 4.2 Data plane

`hqqq-reference-data` is the sole publisher of `refdata.basket.active.v1`.
`hqqq-ingress` is the sole publisher of `market.raw_ticks.v1` and
`market.latest_by_symbol.v1`, and consumes `refdata.basket.active.v1`
to drive its Tiingo subscription mid-session. The legacy `hqqq-api`
monolith is **not** a producer or consumer on any Phase 2 Kafka topic.

```text
Tiingo WebSocket
      |
      v
[hqqq-ingress           ]  ─►  market.raw_ticks.v1, market.latest_by_symbol.v1
[hqqq-reference-data    ]  ─►  refdata.basket.active.v1
                                          │
                                          ▼
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

### 4.4 What still lives in the monolith (reference only)

The monolith is repo-only reference code; nothing in the list below
is part of the Phase 2 runtime path:

- Provider-specific **holdings-source scrape adapters** (Schwab /
  StockAnalysis / AlphaVantage / Nasdaq). `hqqq-reference-data` runs
  on the `File` / `Http` drop pipeline plus the deterministic
  fallback seed today; the legacy scrapers can be ported behind
  `IHoldingsSource` later without touching the refresh pipeline.
- The legacy in-monolith corporate-action implementation — kept as a
  spec reference. The runtime corp-action layer is now Phase-2-native
  inside `hqqq-reference-data` (composite `File`+`Tiingo` provider,
  `CorporateActionAdjustmentService`, `BasketTransitionPlanner`,
  `SymbolRemapResolver`).
- Recorder / benchmark tooling (`Benchmark` module).
- The legacy `/api/system/health` aggregator path. The Phase 2 gateway
  serves `/api/system/health` from the native aggregator; `legacy` and
  `stub` are opt-in fallback modes selectable via
  `Gateway:Sources:SystemHealth`. See §4.5.

### 4.5 Legacy gateway forwarding (opt-in only)

The `legacy` / `stub` source modes on `hqqq-gateway` remain in the
codebase for offline UI smoke (`stub`) or side-by-side parity testing
against a separately-running monolith (`legacy`). They are **not** the
default Phase 2 path. The default per-endpoint posture wired in
`docker-compose.phase2.yml` is `redis` / `redis` / `timescale` /
`aggregated`. When any endpoint resolves to `legacy` the gateway logs
a loud warning at startup so the choice is visible to operators.

### 4.6 Still deferred

- **Phase 3** — Kubernetes app-tier operationalization (`Deployment` +
  `Service` + HPA for stateless services, managed stateful infra
  Kafka/Redis/Postgres kept separate).
- HA topologies for Kafka / Redis / Timescale themselves (D5 only
  duplicates the gateway).
- Multi-instance quote-engine / persistence / ingress / reference-data
  (singletons today by design).
- Wider corp-action coverage: dividends, spin-offs, mergers,
  cross-exchange moves, ISIN/CUSIP-level remaps. Phase 2 implements
  forward / reverse splits, ticker renames, constituent transition
  detection, and scale-factor continuity — explicit and narrow.
- Replay / anomaly / backfill in `hqqq-analytics`.
- Custom domain + TLS, scheduled trigger for the analytics job, image
  signing / SBOMs / vulnerability scans in CI. (Azure Files mount for
  the quote-engine checkpoint is now opt-in via the
  `quoteEngineCheckpointPersistence` bicepparam toggle — see
  [`docs/phase2/azure-deploy.md` §9](phase2/azure-deploy.md).)

---

## 5) Current-to-future evolution summary

| Concern | Phase 1 monolith | Phase 2 now (through D5) | Still deferred |
|---|---|---|---|
| Tick transport | In-process ingestion | `hqqq-ingress` opens the real Tiingo IEX websocket and publishes `market.raw_ticks.v1` + `market.latest_by_symbol.v1`; `hqqq-quote-engine` consumes from there | HA Kafka topology (Phase 3+) |
| Serving state | In-process memory | Redis (`hqqq:snapshot:*`, `hqqq:constituents:*`); multi-gateway-safe by construction (D5) | HA Redis topology (Phase 3+) |
| History storage | Filesystem snapshots | TimescaleDB hypertables + 1m/5m continuous aggregates + retention policies | Rollup-first history reads (C5+) |
| Gateway read path | Monolith endpoints | `hqqq-gateway` with layered per-endpoint source selection (`redis`/`timescale`/`aggregated`/`legacy`/`stub`) | — |
| System-health | Monolith `/api/system/health` only | `hqqq-gateway` native aggregator (D1, default); `legacy`/`stub` available as fallbacks | — |
| Fan-out model | Single-process internal fan-out | Multi-consumer groups over Kafka **plus** live `hqqq:channel:quote-update` Redis pub/sub; every gateway replica subscribes and broadcasts locally (D2 + D5) | HA Kafka / Redis topologies (Phase 3+) |
| Offline analytics | Ad-hoc / notebook | `hqqq-analytics` one-shot report (`Analytics:Mode=report`); Manual-trigger Container Apps Job in Azure | Replay / anomaly / backfill (C5+ / D) |
| Deployment | Local/prototype style | Local compose (infra base + Phase 2 app-tier overlay D3 + replica-smoke overlay D5) **and** Azure Container Apps via Bicep + GitHub OIDC (D4) | Kubernetes app-tier (Phase 3) |
