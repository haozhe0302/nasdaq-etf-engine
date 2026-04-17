# Phase 2 -- Kafka Topic Design

All topic names are defined as constants in
`src/building-blocks/Hqqq.Infrastructure/Kafka/KafkaTopics.cs`.

## Topic inventory

| Topic | Key | Value type | Cleanup | Partitions | Producer | Consumer(s) |
|-------|-----|-----------|---------|------------|----------|-------------|
| `market.raw_ticks.v1` | symbol | `RawTickV1` | delete (time-based) | 1 (Phase 2), scale later | hqqq-ingress | hqqq-quote-engine |
| `market.latest_by_symbol.v1` | symbol | `LatestSymbolQuoteV1` | compact | 1 | hqqq-ingress | hqqq-quote-engine (bootstrap) |
| `refdata.basket.active.v1` | basketId | `BasketActiveStateV1` | compact | 1 | hqqq-reference-data | hqqq-quote-engine, hqqq-ingress |
| `pricing.snapshots.v1` | basketId | `QuoteSnapshotV1` | delete (time-based) | 1 | hqqq-quote-engine | hqqq-gateway, hqqq-persistence |
| `ops.incidents.v1` | service | `IncidentEventV1` | delete (time-based) | 1 | any service | hqqq-analytics |

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

## Phase 2 partition strategy

All topics start with 1 partition for simplicity (single basket family, single ingress instance). Phase 3 scales partitions by symbol hash or basketId for multi-basket / multi-instance deployments.
