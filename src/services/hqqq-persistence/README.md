# hqqq-persistence

Consumes Kafka events and writes to TimescaleDB: raw ticks, quote snapshots,
constituent snapshots, basket versions, and ops samples.

**Future home of current `History` module's write-side.**

## Responsibilities (Phase 2)

- Independent Kafka consumer group
- Write TimescaleDB tables: `raw_ticks`, `quote_snapshots`,
  `constituent_snapshots`, `basket_versions`, `ops_samples`
- Maintain continuous aggregates (1s / 5s / 1m rollups)
- Retention policies: raw ticks short, snapshots longer, rollups longest
