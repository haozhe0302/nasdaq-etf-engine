# hqqq-gateway

REST + SignalR serving gateway. Quote/constituents can be served from Redis
(Phase 2B5) and history can be served from Timescale (Phase 2C2);
system-health remains on the transitional stub/legacy path until a later
observability step. Pure serving layer with no business computation.

**Future home of current API endpoints + MarketHub.**

## Responsibilities (Phase 2)

- REST endpoints: `GET /api/quote`, `GET /api/constituents`,
  `GET /api/history?range=`, `GET /api/system/health`
- WebSocket: `/hubs/market` (SignalR)
- Data sources:
  - B5: Redis for latest quote/constituents snapshots (optional)
  - C2: Timescale (`quote_snapshots`) for history (optional)
  - system-health still served via stub or legacy forwarding
- SignalR Redis backplane for multi-instance fan-out

## Configuration

Gateway source selection is layered: a **global** `Gateway:DataSource` acts as
the fallback for every endpoint, and individual endpoints can be overridden
with `Gateway:Sources:*`. Quote and constituents accept `redis`, history
accepts `timescale`, and system-health still follows only the global switch
(stub/legacy).

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

> `/api/system/health` intentionally does **not** accept `redis` or
> `timescale`. System-health stays on stub/legacy and moves to native
> gateway aggregation in a later observability step.

### B-phase operating modes

The gateway is designed to be run in one of three operating modes during the
Phase 2B cutover. Each mode differs only in configuration.

| Mode | Quote | Constituents | History | System health | Required infra | Intended use |
|------|-------|--------------|---------|---------------|----------------|--------------|
| **Stub mode** — `DataSource=stub` | stub | stub | stub | stub | none | UI smoke / offline dev; deterministic placeholder DTOs, no dependencies |
| **Legacy proxy mode** — `DataSource=legacy` + `LegacyBaseUrl` | legacy | legacy | legacy | legacy (gateway-overlaid) | running `hqqq-api` | Regression parity with Phase 1 while Phase 2 services come online |
| **Mixed B5 cutover mode** — `DataSource=legacy` (or `stub`) + `Sources:Quote=redis` + `Sources:Constituents=redis` | **redis** | **redis** | legacy or stub (follows `DataSource`) | legacy or stub (follows `DataSource`) | Redis + `hqqq-quote-engine` writing snapshots (plus `hqqq-api` if `DataSource=legacy`) | Cutover testing: live quote/constituents off the new pipeline while history/health stay on the transitional path |
| **Mixed C2 cutover mode** — add `Sources:History=timescale` on top of any row above | (as above) | (as above) | **timescale** | (as above) | Timescale + `hqqq-persistence` writing `quote_snapshots` | Cutover testing: serve history directly from Timescale while system-health stays on the transitional path |

Per-endpoint overrides always win over the global `Gateway:DataSource`:
`redis` for quote/constituents, `timescale` for history. System-health
follows only the global switch until a later observability step.

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
