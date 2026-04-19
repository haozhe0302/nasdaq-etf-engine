# Phase 2 -- Kafka Topic Design

All topic names are defined as constants in
`src/building-blocks/Hqqq.Infrastructure/Kafka/KafkaTopics.cs`.

## Topic inventory

| Topic | Key | Value type | Cleanup | Partitions | Producer | Consumer(s) |
|-------|-----|-----------|---------|------------|----------|-------------|
| `market.raw_ticks.v1` | symbol | `RawTickV1` | delete (time-based) | 3 | hqqq-ingress _(stub today; legacy `hqqq-api` publishes in the interim)_ | hqqq-quote-engine, hqqq-persistence |
| `market.latest_by_symbol.v1` | symbol | `LatestSymbolQuoteV1` | compact | 3 | hqqq-ingress _(stub today)_ | hqqq-quote-engine (bootstrap) |
| `refdata.basket.active.v1` | basketId | `BasketActiveStateV1` | compact | 1 | hqqq-reference-data | hqqq-quote-engine, hqqq-ingress |
| `refdata.basket.events.v1` | basketId | `BasketEventV1` | delete (time-based) | 1 | hqqq-reference-data | (reserved — no active consumer today) |
| `pricing.snapshots.v1` | basketId | `QuoteSnapshotV1` | delete (time-based) | 1 | hqqq-quote-engine | hqqq-persistence |
| `ops.incidents.v1` | service | `IncidentEventV1` | delete (time-based) | 1 | _(reserved — no active producer today)_ | _(reserved / deferred — planned for hqqq-analytics)_ |

Notes:

- `pricing.snapshots.v1`: `hqqq-gateway` is **not** a consumer. The gateway
  serves `/api/history` directly from TimescaleDB (`quote_snapshots`) via
  `Gateway:Sources:History=timescale`; `hqqq-persistence` is the only
  runtime consumer today. Analytics reads the persisted Timescale rows
  rather than re-subscribing to Kafka.
- `pricing.snapshots.v1` is **not** the live `/hubs/market` SignalR
  fan-out path either. Live `QuoteUpdate` fan-out uses a separate Redis
  pub/sub channel — `hqqq:channel:quote-update` (D2) — populated by
  `hqqq-quote-engine` and consumed by every `hqqq-gateway` replica
  independently. See [redis-keys.md](redis-keys.md).
- `ops.incidents.v1`: the topic is created by the bootstrap script so
  consumers can attach without a redeploy, but no service publishes or
  subscribes today.

## Naming conventions

- Pattern: `{domain}.{entity}.v{version}`
- Domains: `market`, `refdata`, `pricing`, `ops`
- Version suffix (`v1`) enables schema evolution without breaking consumers

## Compaction policy

- **Compacted topics** (`market.latest_by_symbol.v1`, `refdata.basket.active.v1`) retain only the latest value per key. Consumers can read from offset 0 to reconstruct full state on failover.
- **Time-based topics** use the Kafka default retention (7 days). Adjust via broker config for production.

## Serialization

All events are serialized as JSON using `HqqqJsonDefaults.Options` from `Hqqq.Infrastructure.Serialization`. Future phases may migrate to Avro/Protobuf with Schema Registry (see `KafkaOptions.SchemaRegistryUrl`).

## Consumer groups

Consumer group IDs follow the pattern `{ConsumerGroupPrefix}-{service}-{topic}`, e.g. `hqqq-quote-engine-market.raw_ticks.v1`. The prefix is configurable via `KafkaOptions.ConsumerGroupPrefix`.

`hqqq-persistence` uses the dedicated groups `persistence-snapshots` (for
`pricing.snapshots.v1`) and `persistence-raw-ticks` (for
`market.raw_ticks.v1`) so the two pipelines commit offsets independently.

## Partition strategy (current)

- `market.*` topics run at **3 partitions** already — the bootstrap
  scripts (`scripts/bootstrap-kafka-topics.{ps1,sh}`) create them that way
  so symbol-keyed load can scale horizontally on the ingestion and
  quote-engine side without a topic rebuild.
- `refdata.*`, `pricing.*`, and `ops.*` topics run at **1 partition**
  today — these keys are coarse (basket id, service name) and per-partition
  ordering matters more than throughput at this stage.
- Replication factor is `1` in local dev; production multi-broker sizing
  is a later-phase concern.
