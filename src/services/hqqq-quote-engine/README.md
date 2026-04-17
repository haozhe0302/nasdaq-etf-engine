# hqqq-quote-engine

Internal pricing engine for the HQQQ basket family.

**B3 scope (current):** the engine now consumes real Kafka inputs
(`market.raw_ticks.v1`, `refdata.basket.active.v1`) and persists a
lightweight file-based checkpoint so restarts reinstall the active basket
before the first live message is handled. Output side (Redis latest-state,
`pricing.snapshots.v1` producer, gateway fan-out) remains deferred to B4.

## Internal pipeline

```
Kafka market.raw_ticks.v1        ─► RawTickConsumer        ─┐
Kafka refdata.basket.active.v1   ─► BasketEventConsumer    ─┤
                                                             ▼
                           In-proc sinks ─► QuoteEngineWorker ─► IQuoteEngine ─► PerSymbolQuoteStore
                                                                                 BasketStateStore
                                                                                 EngineRuntimeState
                                                                                     │
                                                                                     ├─► SnapshotMaterializer ─► QuoteSnapshotDto (logged)
                                                                                     └─► QuoteDeltaMaterializer ─► QuoteUpdateDto (logged)

EngineCheckpointRestorer (startup, pre-consumer) ─► engine.OnBasketActivated
QuoteEngineWorker ─► FileEngineCheckpointStore (on activation + interval)
```

Pure pricing math (raw basket value, scale-factor calibration, top movers,
freshness summary) lives in `Hqqq.Domain/Services` so it is reusable by the
future analytics service without dragging engine state along.

## Responsibilities

### B2 (previous) — engine core

- Per-symbol quote state, basket state, runtime NAV scalars.
- Full `QuoteSnapshotDto` and slim `QuoteUpdateDto` materialization.
- Deterministic under test without Kafka or Redis.

### B3 (this phase) — real inputs + crash recovery

- Consume `market.raw_ticks.v1` via `RawTickConsumer` into the engine's
  normalized-tick sink.
- Consume the richer `BasketActiveStateV1` payload on the compacted
  `refdata.basket.active.v1` topic via `BasketEventConsumer` (carries
  constituents + pricing basis + scale factor inline so no synchronous
  HTTP call to reference-data is needed). Fingerprint-aware guard
  prevents blending across basket versions on replay.
- Persist a lightweight engine checkpoint
  (basket identity + pricing basis + scale factor + last snapshot digest)
  via `FileEngineCheckpointStore`. Path is configurable via
  `QuoteEngine:CheckpointPath` (default `./data/quote-engine/checkpoint.json`).
- `EngineCheckpointRestorer` runs during `IHostedService.StartAsync`, before
  any consumer or worker spins up, so the engine is hydrated with the
  previously-active basket before live messages arrive.
- Startup is tolerant: missing Kafka, missing checkpoint, or corrupt
  checkpoint never fail the process.

### Deferred

- **B4**: gateway REST + SignalR fan-out; `pricing.snapshots.v1` producer;
  Redis cache writes; corporate-action + reference-anchor services.
- **B5**: Timescale persistence + history.

## Configuration

Bound from the `QuoteEngine` section:

```json
"QuoteEngine": {
  "CheckpointPath": "./data/quote-engine/checkpoint.json",
  "CheckpointInterval": "00:00:10",
  "RawTicksTopic": "market.raw_ticks.v1",
  "BasketActiveTopic": "refdata.basket.active.v1"
}
```

Kafka connection settings come from the shared `Kafka` section
(`BootstrapServers`, `ClientId`, `ConsumerGroupPrefix`). Consumer groups
are derived as `{prefix}-quote-engine-ticks` and
`{prefix}-quote-engine-baskets`.

## HA model

Single logical owner per basket family in Phase 2 — no cross-instance
distributed basket aggregation, no basket sharding. HA is delivered later
via Kafka consumer-group rebalance (single active consumer per basketId).
