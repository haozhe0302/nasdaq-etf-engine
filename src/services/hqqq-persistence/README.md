# hqqq-persistence

Consumes Kafka events and writes history to TimescaleDB. Phase 2C rolls out
in narrow slices aligned with gateway read-side cutovers.

## C3 status (current)

Implemented:

- Consumes `pricing.snapshots.v1` (group `persistence-snapshots`) with
  the shared Kafka/JSON infra and writes to `quote_snapshots`.
- Consumes `market.raw_ticks.v1` (group `persistence-raw-ticks`) and
  writes to `raw_ticks`.
- Ensures the `quote_snapshots` and `raw_ticks` hypertables,
  `quote_snapshots_1m` / `quote_snapshots_5m` continuous-aggregate rollups,
  and `add_retention_policy` on all four at startup via idempotent DDL.
  Toggle with `Persistence:SchemaBootstrapOnStart`.
- Batched, transactional inserts with replay-safe
  `ON CONFLICT ... DO NOTHING`:
  - `quote_snapshots`: `(basket_id, ts)`
  - `raw_ticks`: `(symbol, provider_timestamp, sequence)`
- Two fully isolated pipelines (separate consumer, channel, worker, writer,
  and failure counters) so a raw-tick write failure cannot impact
  snapshot writes, and vice versa.

Deferred to later phases:

- `constituent_snapshots` table and per-symbol history.
- Re-pointing the gateway read-side at rollups.
- `basket_versions` persistence.
- Anomaly detection / analytics consumers.

## Pipelines

```
pricing.snapshots.v1                   market.raw_ticks.v1
        │                                       │
        ▼                                       ▼
  QuoteSnapshotConsumer                   RawTickConsumer
        │                                       │
        ▼                                       ▼
  InMemoryQuoteSnapshotFeed               InMemoryRawTickFeed
  (bounded Channel<QuoteSnapshotV1>)      (bounded Channel<RawTickV1>)
        │                                       │
        ▼                                       ▼
  QuoteSnapshotPersistenceWorker          RawTickPersistenceWorker
  (batch on size OR flush interval)       (batch on size OR flush interval)
        │                                       │
        ▼                                       ▼
  TimescaleQuoteSnapshotWriter            TimescaleRawTickWriter
  (INSERT ... ON CONFLICT DO NOTHING)     (INSERT ... ON CONFLICT DO NOTHING)
        │                                       │
        ▼                                       ▼
  quote_snapshots                         raw_ticks
  (hypertable, partitioned on ts)         (hypertable, partitioned on
        │                                  provider_timestamp)
        ├── quote_snapshots_1m  (continuous aggregate)
        └── quote_snapshots_5m  (continuous aggregate)
```

## Configuration

### `Kafka`

Shared `KafkaOptions`. Topic names are **not** duplicated here; they are
the central `KafkaTopics.PricingSnapshots` and `KafkaTopics.RawTicks`
constants.

### `Timescale`

Shared `TimescaleOptions.ConnectionString`. A single `NpgsqlDataSource`
is registered for every schema bootstrapper and every writer to share.

### `Persistence`

Narrow, persistence-only knobs:

| Key | Default | Purpose |
|-----|---------|---------|
| `SchemaBootstrapOnStart` | `true` | Run idempotent DDL + rollup + retention-policy bootstrap at startup. Disable in environments where schema is owned externally. |
| `SnapshotWriteBatchSize` | `128` | Maximum snapshot rows per transactional flush. |
| `SnapshotFlushInterval` | `00:00:00.500` | Upper bound on flush latency at low snapshot ingest rates. |
| `SnapshotChannelCapacity` | `2048` | Bounded in-proc buffer between snapshot consumer and worker. |
| `RawTickWriteBatchSize` | `256` | Maximum raw-tick rows per transactional flush. |
| `RawTickFlushInterval` | `00:00:00.500` | Upper bound on flush latency at low raw-tick ingest rates. |
| `RawTickChannelCapacity` | `8192` | Bounded in-proc buffer between raw-tick consumer and worker. Larger than the snapshot channel because raw ticks arrive per provider update, not per compute cycle. |
| `RawTickRetention` | `30.00:00:00` | `add_retention_policy` window on `raw_ticks`. |
| `QuoteSnapshotRetention` | `365.00:00:00` | `add_retention_policy` window on `quote_snapshots`. |
| `RollupRetention` | `730.00:00:00` | `add_retention_policy` window on `quote_snapshots_1m` and `quote_snapshots_5m`; longer than the base snapshots so rollups outlive the raw snapshots they were built from. |

## Idempotency model

- `quote_snapshots`: `(basket_id, ts)` is `UNIQUE`. On replay, the
  `ON CONFLICT ... DO NOTHING` clause silently discards duplicates so
  the first-seen `inserted_at_utc` is preserved. The quote-engine
  produces one snapshot per compute cycle per basket, so this pair is
  naturally immutable.
- `raw_ticks`: `(symbol, provider_timestamp, sequence)` is `UNIQUE`.
  Rationale:
  - `sequence` is monotonic per-provider per-symbol stream, so an event
    replay lands on the same row rather than appending a duplicate.
  - `provider_timestamp` anchors the key on the natural time axis and
    is the column the hypertable is partitioned on — the chunk router
    never needs to scan multiple chunks to enforce uniqueness.
  - `provider` is intentionally **not** part of the key today because
    Phase 2 ingress is single-provider (Tiingo). Adding it later is a
    purely additive index change if/when a second provider is introduced.

## Rollups (groundwork)

Two TimescaleDB **continuous aggregates** are materialized on top of
`quote_snapshots`:

- `quote_snapshots_1m` — 1-minute buckets
- `quote_snapshots_5m` — 5-minute buckets

Both store:

- `bucket` — `time_bucket('1 minute' / '5 minutes', ts)`
- `basket_id`
- representative NAV: `last(nav, ts)`
- representative market-proxy price: `last(market_proxy_price, ts)`
- representative premium/discount: `last(premium_discount_pct, ts)`
- `point_count` — `count(*)` inside the bucket
- freshness summary: `avg(max_component_age_ms)`, `sum(stale_count)`,
  `sum(fresh_count)`

Background refresh is handled by `add_continuous_aggregate_policy` —
no custom scheduler. The `/api/history` read-side still serves from the
raw `quote_snapshots` table; these rollups are groundwork for future
long-range analytics queries only.

## Retention

`add_retention_policy(..., if_not_exists => TRUE)` is registered at
startup for every hypertable and continuous aggregate. Windows are
built from `PersistenceOptions` and formatted into PostgreSQL
`INTERVAL` literals by `RetentionPolicySchemaSql.FormatInterval` — so
sub-day windows are expressible (hours / minutes / seconds) without
rounding to zero.

## Error handling and failure isolation

- Malformed Kafka payloads (null value, empty key fields, default
  timestamps, deserialization failure) are logged and skipped on both
  consumers. Neither consumer loop crashes on bad data.
- Timescale write failures are logged at error and the batch is retained
  for retry. Each worker has its own `ConsecutiveFailureCount` and
  `TotalFailureCount` so raw-tick and snapshot pipeline health is
  observable independently.
- Schema bootstrap failures at startup **are fatal** (fail fast). We do
  not want to begin consuming when any destination table, rollup, or
  retention policy is missing.

## Running locally

```bash
# 1. Start Timescale + Kafka
docker compose up -d db kafka

# 2. Create Kafka topics
./scripts/bootstrap-kafka-topics.sh   # or .ps1 on Windows

# 3. Run the service
dotnet run --project src/services/hqqq-persistence
```
