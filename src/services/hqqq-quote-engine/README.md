# hqqq-quote-engine

Internal pricing engine for the HQQQ basket family.

**B2 scope (current):** explicit in-memory state ownership + deterministic
snapshot / delta materialization aligned with the frontend serving contract
in `Hqqq.Contracts.Dtos`. No downstream transport is wired yet — the worker
drains in-memory feeds and logs the materialized values.

## Internal pipeline

```
IRawTickFeed       ─┐
                    ├─► QuoteEngineWorker ─► IQuoteEngine ─► PerSymbolQuoteStore
IBasketStateFeed   ─┘                                        BasketStateStore
                                                             EngineRuntimeState
                                                                 │
                                                                 ├─► SnapshotMaterializer ─► QuoteSnapshotDto
                                                                 └─► QuoteDeltaMaterializer ─► QuoteUpdateDto
```

Pure pricing math (raw basket value, scale-factor calibration, top movers,
freshness summary) lives in `Hqqq.Domain/Services` so it is reusable by the
future analytics service without dragging engine state along.

## Responsibilities

### B2 (this phase)

- Accept normalized ticks via `IRawTickFeed` (in-memory channel).
- Accept active basket + pricing basis + scale factor via `IBasketStateFeed`.
- Maintain per-symbol quote state, active basket state, and runtime NAV scalars.
- Materialize full `QuoteSnapshotDto` and slim `QuoteUpdateDto` on demand.
- Run deterministically under test without Kafka or Redis.

### Deferred

- **B3**: Kafka consumers for `market.raw_ticks.v1`, `market.latest_by_symbol.v1`,
  `refdata.basket.active.v1`; Redis cache writes; Kafka producer for
  `pricing.snapshots.v1`; full bootstrap / activation state machine;
  corporate-action + reference-anchor services.
- **B4**: gateway REST + SignalR fan-out.
- **B5**: Timescale persistence + history.

## HA model

Single logical owner per basket family in Phase 2 — no cross-instance
distributed basket aggregation, no basket sharding. HA is delivered later
via Kafka consumer-group rebalance (single active consumer per basketId).
