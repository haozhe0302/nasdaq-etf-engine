# hqqq-gateway

REST + SignalR serving gateway. Quote/constituents can be served from Redis
(Phase 2B5), history can be served from Timescale (Phase 2C2), and as of
Phase 2D1 `/api/system/health` is served by a native aggregator that scrapes
each Phase 2 service's `/healthz/ready` plus the local Redis/Timescale
probes. Pure serving layer with no business computation.

**Future home of current API endpoints + MarketHub.**

## Responsibilities (Phase 2)

- REST endpoints: `GET /api/quote`, `GET /api/constituents`,
  `GET /api/history?range=`, `GET /api/system/health`
- Management endpoints (Phase 2D1): `GET /healthz/live`,
  `GET /healthz/ready`, `GET /metrics`
- WebSocket: `/hubs/market` (SignalR) — broadcasts the slim `QuoteUpdate`
  event (Phase 2D2)
- Data sources:
  - B5: Redis for latest quote/constituents snapshots (optional)
  - C2: Timescale (`quote_snapshots`) for history (optional)
  - D1: native aggregation for system-health (default; `legacy`/`stub` available for cutover/offline)
  - D2: Redis pub/sub (`hqqq:channel:quote-update`) → SignalR fan-out

## Live fan-out (Phase 2D2)

`/hubs/market` is wired to `QuoteUpdateSubscriber`, a hosted background
service that subscribes to the `hqqq:channel:quote-update` Redis pub/sub
channel populated by `hqqq-quote-engine`. Each gateway instance receives
every published `QuoteUpdateEnvelope`, validates the JSON, and broadcasts
the inner `QuoteUpdateDto` to its own connected SignalR clients using the
locked event name `QuoteUpdate`. This makes the fan-out path
multi-gateway-safe without requiring a SignalR Redis backplane: every
gateway sees the same message and dispatches locally. Reconnect /
bootstrap remains REST `GET /api/quote` — D2 deliberately does not add a
replay buffer or sequence protocol. Malformed payloads are dropped
silently and counted via `hqqq.gateway.quote_updates_malformed`.

## Configuration

Gateway source selection is layered: a **global** `Gateway:DataSource` acts as
the fallback for every endpoint, and individual endpoints can be overridden
with `Gateway:Sources:*`. Quote and constituents accept `redis`, history
accepts `timescale`, and system-health defaults to `aggregated` (native
fan-out across the Phase 2 services and local infra probes) — `legacy` and
`stub` remain available via `Gateway:Sources:SystemHealth` for cutover and
offline scenarios.

### `Gateway:DataSource` (global fallback)

Used when an endpoint has no per-endpoint override.

| Value | Behavior |
|-------|----------|
| `stub` | Return deterministic placeholder DTOs (HTTP 200). Default. |
| `legacy` | Forward requests to legacy `hqqq-api` via HttpClient. |
| _(empty)_ | Auto-select: `legacy` if `Gateway:LegacyBaseUrl` is set and env is Development; otherwise `stub`. |

### `Gateway:Sources:Quote` / `Gateway:Sources:Constituents`

Per-endpoint overrides for `GET /api/quote` and `GET /api/constituents`.

| Value | Behavior |
|-------|----------|
| `stub` | Return deterministic placeholder DTOs (HTTP 200). |
| `legacy` | Forward the request to legacy `hqqq-api`. |
| `redis` | Read the latest snapshot from Redis (`hqqq:snapshot:{basketId}` / `hqqq:constituents:{basketId}`). |
| _(empty)_ | Inherit `Gateway:DataSource`. |

In `redis` mode, if the Redis key is missing the gateway returns a controlled
degraded response (HTTP 503, JSON body `{"error":"quote_unavailable", ...}` /
`{"error":"constituents_unavailable", ...}`). Malformed payloads return
HTTP 502 with `{"error":"quote_malformed"}` / `{"error":"constituents_malformed"}`.
The gateway never silently substitutes stub data when `redis` was requested.

### `Gateway:Sources:History`

Per-endpoint override for `GET /api/history?range=...`. Supported in C2.

| Value | Behavior |
|-------|----------|
| `stub` | Return a deterministic placeholder history payload (HTTP 200). |
| `legacy` | Forward the request to legacy `hqqq-api` preserving the `range` query string. |
| `timescale` | Read `quote_snapshots` from TimescaleDB and compose the response directly in the gateway. |
| _(empty)_ | Inherit `Gateway:DataSource` (stub/legacy only; the global switch never resolves to `timescale`). |

Supported ranges: `1D`, `5D`, `1M`, `3M`, `YTD`, `1Y`. Any other value returns
HTTP 400 with `{"error":"history_range_unsupported","range":"...","supported":[...]}`.
An empty window returns HTTP 200 with a render-safe empty payload (zeroed
`pointCount`/`totalPoints`, empty `series`, stable 21-bucket `distribution`).
Timescale query failures return HTTP 503 with
`{"error":"history_unavailable",...}`; the gateway never silently substitutes
stub data when `timescale` was requested.

The response preserves the existing frontend contract exactly:
`range, startDate, endDate, pointCount, totalPoints, isPartial, series[time,nav,marketPrice], trackingError, distribution, diagnostics`.

### `Gateway:Sources:SystemHealth`

Per-endpoint override for `GET /api/system/health`. Defaults to
`aggregated` (Phase 2D1) regardless of the global `DataSource`.

| Value | Behavior |
|-------|----------|
| `aggregated` | Native fan-out: probes `/healthz/ready` on each configured downstream service in parallel, runs the local `HealthCheckService` for Redis/Timescale, composes the response in `BSystemHealth` shape. **Default.** |
| `legacy` | Forward the request to legacy `hqqq-api` and additively overlay gateway metadata (preserves the Phase 2B1 transitional behavior). |
| `stub` | Return a deterministic placeholder (used for offline UI smoke). |
| _(empty)_ | `aggregated`. |

In `aggregated` mode the gateway never returns a non-200 unless the
gateway itself catastrophically fails. A missing or unreachable downstream
becomes a dependency entry with `status: "unknown"` (and a short
`details` string) instead of crashing the request. A downstream that has
no configured `BaseUrl` becomes `status: "idle"` with
`details: "not configured"`. The roll-up rule collapses any `unhealthy`
or `degraded` dependency into top-level `degraded` so a single missing
worker doesn't trip frontend alarms.

### `Gateway:Health` (aggregator settings)

Bound only when `Gateway:Sources:SystemHealth` resolves to `aggregated`.

| Key | Default | Purpose |
|-----|---------|---------|
| `RequestTimeoutSeconds` | `1.5` | Per-call timeout for each downstream probe. |
| `IncludeRedis` | `true` | Include the local Redis health check in the payload. |
| `IncludeTimescale` | `true` | Include the local Timescale health check in the payload. |
| `Services:{Key}:BaseUrl` | _empty_ | Base URL of each downstream service's management endpoint. Keys: `ReferenceData`, `Ingress`, `QuoteEngine`, `Persistence`, `Analytics`. Empty/missing → reported as `idle` (not configured). |

### B-phase operating modes

The gateway is designed to be run in one of three operating modes during the
Phase 2B cutover. Each mode differs only in configuration.

| Mode | Quote | Constituents | History | System health | Required infra | Intended use |
|------|-------|--------------|---------|---------------|----------------|--------------|
| **Stub mode** — `DataSource=stub` + `Sources:SystemHealth=stub` | stub | stub | stub | stub | none | UI smoke / offline dev; deterministic placeholder DTOs, no dependencies |
| **Legacy proxy mode** — `DataSource=legacy` + `LegacyBaseUrl` + `Sources:SystemHealth=legacy` | legacy | legacy | legacy | legacy (gateway-overlaid) | running `hqqq-api` | Regression parity with Phase 1 while Phase 2 services come online |
| **D1 native mode (default)** — anything above without an explicit `Sources:SystemHealth` override | (as configured) | (as configured) | (as configured) | **aggregated** | Phase 2 service `/healthz/ready` endpoints reachable on `Gateway:Health:Services:*:BaseUrl` (plus Redis/Timescale for the local probes) | Standard Phase 2 posture: native system-health off the new pipeline; missing services surface as `unknown`/`idle` without crashing |
| **Mixed B5 cutover mode** — `Sources:Quote=redis` + `Sources:Constituents=redis` | **redis** | **redis** | (as configured) | (as configured) | Redis + `hqqq-quote-engine` writing snapshots | Cutover testing: live quote/constituents off the new pipeline |
| **Mixed C2 cutover mode** — add `Sources:History=timescale` on top of any row above | (as above) | (as above) | **timescale** | (as above) | Timescale + `hqqq-persistence` writing `quote_snapshots` | Cutover testing: serve history directly from Timescale |

Per-endpoint overrides always win over the global `Gateway:DataSource`:
`redis` for quote/constituents, `timescale` for history, `aggregated` /
`legacy` / `stub` for system-health.

### `Gateway:BasketId`

Basket identifier used to format Redis keys for quote/constituents sources.
Defaults to `HQQQ` (matches the seed basket in `hqqq-reference-data`).

### `Gateway:LegacyBaseUrl`

Base URL of the legacy `hqqq-api` instance (e.g. `http://localhost:5000`).
Required when any endpoint resolves to `legacy` (globally or per-endpoint) or
when relying on auto-detection in Development.

### Examples

```bash
# Explicit stub mode (default, no infra needed)
Gateway__DataSource=stub

# Legacy proxy mode — forward to running hqqq-api
Gateway__DataSource=legacy
Gateway__LegacyBaseUrl=http://localhost:5000

# Auto-detect in Development: legacy if LegacyBaseUrl is set
Gateway__LegacyBaseUrl=http://localhost:5000

# B5 mixed mode: quote/constituents from Redis, history/health still stub
Gateway__Sources__Quote=redis
Gateway__Sources__Constituents=redis
Gateway__BasketId=HQQQ
Redis__Configuration=localhost:6379

# B5 partial cut-over: quote from Redis, everything else from legacy
Gateway__DataSource=legacy
Gateway__LegacyBaseUrl=http://localhost:5000
Gateway__Sources__Quote=redis
Redis__Configuration=localhost:6379

# C2 history cut-over: history from Timescale, quote/constituents still legacy
Gateway__DataSource=legacy
Gateway__LegacyBaseUrl=http://localhost:5000
Gateway__Sources__History=timescale
Gateway__BasketId=HQQQ
Timescale__ConnectionString=Host=localhost;Port=5432;Database=hqqq;Username=admin;Password=changeme

# C2 stacked cut-over: quote/constituents via Redis, history via Timescale
Gateway__DataSource=legacy
Gateway__LegacyBaseUrl=http://localhost:5000
Gateway__Sources__Quote=redis
Gateway__Sources__Constituents=redis
Gateway__Sources__History=timescale
Redis__Configuration=localhost:6379
Timescale__ConnectionString=Host=localhost;Port=5432;Database=hqqq;Username=admin;Password=changeme
```
