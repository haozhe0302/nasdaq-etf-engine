# hqqq-quote-engine

Internal pricing engine for the HQQQ basket family.

**B4 scope (current):** the engine now consumes real Kafka inputs
(`market.raw_ticks.v1`, `refdata.basket.active.v1`), persists a lightweight
file-based checkpoint so restarts reinstall the active basket before the
first live message is handled, and materializes its outputs to the serving
layer: Redis latest-state for the basket's quote + constituents snapshot,
and `pricing.snapshots.v1` on Kafka for downstream persistence / analytics
consumers. Gateway Redis readers for REST quote/constituents are now live in B5;
live SignalR fan-out is in place since D2 — the engine publishes
`QuoteUpdateEnvelope` JSON payloads to the Redis pub/sub channel
`hqqq:channel:quote-update` (`RedisKeys.QuoteUpdateChannel`) for each
basket cycle, and every `hqqq-gateway` replica subscribes independently
and broadcasts the inner `QuoteUpdate` DTO to its own SignalR clients
(no SignalR Redis backplane required).

## Internal pipeline

```
Kafka market.raw_ticks.v1        ─► RawTickConsumer        ─┐
Kafka refdata.basket.active.v1   ─► BasketEventConsumer    ─┤
                                                             ▼
                           In-proc sinks ─► QuoteEngineWorker ─► IQuoteEngine ─► PerSymbolQuoteStore
                                                                                 BasketStateStore
                                                                                 EngineRuntimeState
                                                                                     │
                                                                                     ├─► SnapshotMaterializer ─► QuoteSnapshotDto ─┬─► RedisSnapshotWriter ─► Redis hqqq:snapshot:{basketId}
                                                                                     │                                              └─► QuoteSnapshotV1Mapper ─► SnapshotTopicPublisher ─► Kafka pricing.snapshots.v1
                                                                                     ├─► ConstituentsSnapshotMaterializer ─► ConstituentsSnapshotDto ─► RedisConstituentsWriter ─► Redis hqqq:constituents:{basketId}
                                                                                     └─► QuoteDeltaMaterializer ─► QuoteUpdateDto ─► RedisQuoteUpdatePublisher ─► Redis pub/sub hqqq:channel:quote-update (D2; gateway replicas broadcast to /hubs/market)

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

### B3 — real inputs + crash recovery

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

### B4 (this phase) — downstream outputs

- Materialize the latest quote snapshot to Redis under the namespaced key
  `hqqq:snapshot:{basketId}` via `RedisSnapshotWriter`. Redis is the
  serving layer, not a source of truth — writes overwrite on every
  materialize cycle.
- Materialize the latest constituents snapshot (holdings + concentration +
  quality + source provenance) to Redis under `hqqq:constituents:{basketId}`
  via `RedisConstituentsWriter`. Shape matches the frontend
  `BConstituentSnapshot` adapter so the gateway reader in B5 deserializes
  the cached JSON directly.
- Publish `QuoteSnapshotV1` to `pricing.snapshots.v1` (key = `basketId`)
  via `SnapshotTopicPublisher`. The publisher depends on an
  `IPricingSnapshotProducer` seam; the production `ConfluentPricingSnapshotProducer`
  wraps a single long-lived `IProducer<string, QuoteSnapshotV1>` with the
  shared `JsonValueSerializer<T>`, reusing `KafkaConfigBuilder` idempotent-producer
  defaults.
- Sink failures are isolated: each of Redis-snapshot / Redis-constituents /
  Kafka-publish is wrapped in its own `try`/`catch` inside the materialize
  loop. One sink failing never blocks the others or crashes the worker.
- Engine math and state (`IncrementalNavCalculator`, `SnapshotMaterializer`,
  `QuoteDeltaMaterializer`, `PerSymbolQuoteStore`, `BasketStateStore`,
  `EngineRuntimeState`) stay free of sink or producer dependencies. Only
  the worker knows about publishing.

### Deferred

- **Post-B5**: gateway SignalR live fan-out/backplane (`/hubs/market`);
  Timescale persistence of `pricing.snapshots.v1` + history; corporate-action
  and reference-anchor services.

## Configuration

Bound from the `QuoteEngine` section:

```json
"QuoteEngine": {
  "CheckpointPath": "./data/quote-engine/checkpoint.json",
  "CheckpointInterval": "00:00:10",
  "RawTicksTopic": "market.raw_ticks.v1",
  "BasketActiveTopic": "refdata.basket.active.v1",
  "PricingSnapshotsTopic": "pricing.snapshots.v1",
  "MaterializeInterval": "00:00:01"
}
```

Kafka connection settings come from the shared `Kafka` section
(`BootstrapServers`, `ClientId`, `ConsumerGroupPrefix`). Consumer groups
are derived as `{prefix}-quote-engine-ticks` and
`{prefix}-quote-engine-baskets`. Optional SASL/SSL auth
(`SecurityProtocol`, `SaslMechanism`, `SaslUsername`, `SaslPassword`)
plus `EnableTopicBootstrap=false` are honoured for Azure Event Hubs
Kafka — see [`docs/phase2/config-matrix.md`](../../../docs/phase2/config-matrix.md)
for the full env-var surface.

Redis connection settings come from the shared `Redis` section
(`Configuration`). The eager `AddHqqqRedisConnection` registration fails
fast at startup if Redis is unreachable, so a missing cache is surfaced
before the first materialize cycle writes silently into the void.

`QuoteEngine:CheckpointPath` is the seam used to point the checkpoint
file at a persistent volume in container deployments. The default
(`./data/quote-engine/checkpoint.json`) suits local `dotnet run`;
`docker-compose.phase2.yml` overrides it to `/data/quote-engine/checkpoint.json`
on a named volume; on Azure the deploy template overrides it to
`/mnt/quote-engine/checkpoint.json` when the
`quoteEngineCheckpointPersistence` bicepparam toggle is on (Azure Files
mount, durable across revision swaps), and to
`/tmp/quote-engine/checkpoint.json` when the toggle is off (ephemeral,
matches the original D4 posture). Engine code is unchanged across all
four cases — see [`docs/phase2/azure-deploy.md` §9](../../../docs/phase2/azure-deploy.md)
for the Azure operator walkthrough.

## HA model

Single logical owner per basket family in Phase 2 — no cross-instance
distributed basket aggregation, no basket sharding. HA is delivered later
via Kafka consumer-group rebalance (single active consumer per basketId).
