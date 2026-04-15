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
