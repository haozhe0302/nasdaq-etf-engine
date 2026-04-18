# hqqq-gateway

REST + SignalR serving gateway. In B5, serves quote/constituents from Redis
when configured; history and system-health remain on transitional
stub/legacy paths until later phases. Pure serving layer with no business
computation.

**Future home of current API endpoints + MarketHub.**

## Responsibilities (Phase 2)

- REST endpoints: `GET /api/quote`, `GET /api/constituents`,
  `GET /api/history?range=`, `GET /api/system/health`
- WebSocket: `/hubs/market` (SignalR)
- Data sources (B5): Redis for latest quote/constituents snapshots (optional);
  history/system-health still served via stub or legacy forwarding
- SignalR Redis backplane for multi-instance fan-out

## Configuration

Gateway source selection is layered: a **global** `Gateway:DataSource` acts as
the fallback for every endpoint, and individual endpoints can be overridden
with `Gateway:Sources:*`. History and system-health stay on the global switch
for now — only quote and constituents can be switched to `redis` in B5.

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

> `/api/history` and `/api/system/health` intentionally do **not** accept
> `redis` in B5. History moves to Timescale in C3; system-health moves to
> native gateway aggregation in a later observability step.

### B-phase operating modes

The gateway is designed to be run in one of three operating modes during the
Phase 2B cutover. Each mode differs only in configuration.

| Mode | Quote | Constituents | History | System health | Required infra | Intended use |
|------|-------|--------------|---------|---------------|----------------|--------------|
| **Stub mode** — `DataSource=stub` | stub | stub | stub | stub | none | UI smoke / offline dev; deterministic placeholder DTOs, no dependencies |
| **Legacy proxy mode** — `DataSource=legacy` + `LegacyBaseUrl` | legacy | legacy | legacy | legacy (gateway-overlaid) | running `hqqq-api` | Regression parity with Phase 1 while Phase 2 services come online |
| **Mixed B5 cutover mode** — `DataSource=legacy` (or `stub`) + `Sources:Quote=redis` + `Sources:Constituents=redis` | **redis** | **redis** | legacy or stub (follows `DataSource`) | legacy or stub (follows `DataSource`) | Redis + `hqqq-quote-engine` writing snapshots (plus `hqqq-api` if `DataSource=legacy`) | Cutover testing: live quote/constituents off the new pipeline while history/health stay on the transitional path |

Per-endpoint `redis` on `Gateway:Sources:*` always wins over the global
`Gateway:DataSource` for quote/constituents. History and system-health
follow only the global switch until Phase 2C3 / later observability work.

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
```
