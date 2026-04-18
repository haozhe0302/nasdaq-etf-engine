# hqqq-persistence

Consumes Kafka events and writes history to TimescaleDB. Phase 2C rolls out
in narrow slices aligned with gateway read-side cutovers.

## C1 status (current)

Implemented:

- Consumes `pricing.snapshots.v1` with the shared Kafka/JSON infra.
- Ensures the `quote_snapshots` hypertable (+ read-side index) at startup
  via idempotent DDL. Toggle with `Persistence:SchemaBootstrapOnStart`.
- Batched, transactional inserts into `quote_snapshots` with
  `ON CONFLICT (basket_id, ts) DO NOTHING` — safe to replay.
- Decoupled pipeline: Kafka consumer → bounded in-proc channel → batching
  worker → Timescale writer, so backpressure is explicit and unit tests run
  without a live broker or database.

Not in C1 (deferred):

- `market.raw_ticks.v1` consumption and `raw_ticks` table — **C2**.
- `constituent_snapshots` table and per-symbol history — **C2**.
- Gateway `/api/history` read-side — **C3** (depends on C1 data being written).
- Continuous aggregates, rollups, and retention policies — **C4**.

## Pipeline

```
pricing.snapshots.v1
        │
        ▼
  QuoteSnapshotConsumer   (validates, drops malformed)
        │
        ▼
  InMemoryQuoteSnapshotFeed   (bounded Channel<QuoteSnapshotV1>)
        │
        ▼
  QuoteSnapshotPersistenceWorker   (batch on size OR flush interval)
        │
        ▼
  TimescaleQuoteSnapshotWriter   (INSERT ... ON CONFLICT DO NOTHING)
        │
        ▼
  quote_snapshots   (hypertable, partitioned on ts)
```

## Configuration

### `Kafka`

Shared `KafkaOptions`. The topic name is **not** duplicated here; it is the
central `KafkaTopics.PricingSnapshots` constant.

### `Timescale`

Shared `TimescaleOptions.ConnectionString`. A single `NpgsqlDataSource` is
registered for the schema bootstrapper and the writer to share.

### `Persistence`

Narrow, persistence-only knobs:

| Key | Default | Purpose |
|-----|---------|---------|
| `SchemaBootstrapOnStart` | `true` | Run idempotent DDL at startup. Disable in environments where schema is owned externally. |
| `SnapshotWriteBatchSize` | `128` | Maximum rows per transactional flush. |
| `SnapshotFlushInterval` | `00:00:00.500` | Upper bound on flush latency at low ingest rates. |
| `SnapshotChannelCapacity` | `2048` | Bounded in-proc buffer between consumer and worker. |

## Idempotency model

`(basket_id, ts)` is declared `UNIQUE` in `quote_snapshots`. On replay, the
`ON CONFLICT ... DO NOTHING` clause silently discards duplicates so the
first-seen `inserted_at_utc` is preserved. The quote-engine produces one
snapshot per compute cycle per basket, so this pair is naturally immutable.

## Error handling

- Malformed Kafka payloads (null, empty `BasketId`, default `Timestamp`,
  empty `QuoteQuality`, deserialization failure) are logged and skipped.
  The consumer loop does not crash.
- Timescale write failures are logged at error and the batch is retained
  for retry. `QuoteSnapshotPersistenceWorker.ConsecutiveFailureCount` and
  `TotalFailureCount` are observable so repeated failures are not silently
  swallowed.
- Schema bootstrap failures at startup **are fatal** (fail fast). We do not
  want to begin consuming when the destination table does not exist.

## Running locally

```bash
# 1. Start Timescale + Kafka
docker compose up -d db kafka

# 2. Create Kafka topics
./scripts/bootstrap-kafka-topics.sh   # or .ps1 on Windows

# 3. Run the service
dotnet run --project src/services/hqqq-persistence
```
