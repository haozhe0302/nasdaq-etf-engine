# hqqq-gateway

REST + SignalR serving gateway. Reads Redis for latest state and TimescaleDB
for history. Pure serving layer with no business computation.

**Future home of current API endpoints + MarketHub.**

## Responsibilities (Phase 2)

- REST endpoints: `GET /api/quote`, `GET /api/constituents`,
  `GET /api/history?range=`, `GET /api/system/health`
- WebSocket: `/hubs/market` (SignalR)
- Data sources: Redis for latest snapshot/constituents, TimescaleDB for history
- SignalR Redis backplane for multi-instance fan-out

## Configuration

### `Gateway:DataSource`

Controls which adapter implementation serves each endpoint.

| Value | Behavior |
|-------|----------|
| `stub` | Return deterministic placeholder DTOs (HTTP 200). Default. |
| `legacy` | Forward requests to legacy `hqqq-api` via HttpClient. |
| _(empty)_ | Auto-select: `legacy` if `Gateway:LegacyBaseUrl` is set and env is Development; otherwise `stub`. |

### `Gateway:LegacyBaseUrl`

Base URL of the legacy `hqqq-api` instance (e.g. `http://localhost:5000`).
Required when `DataSource=legacy` or when relying on auto-detection in Development.

### Examples

```bash
# Explicit stub mode (default, no infra needed)
Gateway__DataSource=stub

# Legacy proxy mode — forward to running hqqq-api
Gateway__DataSource=legacy
Gateway__LegacyBaseUrl=http://localhost:5000

# Auto-detect in Development: legacy if LegacyBaseUrl is set
Gateway__LegacyBaseUrl=http://localhost:5000
```
