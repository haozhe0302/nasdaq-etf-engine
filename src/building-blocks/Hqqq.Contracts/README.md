# Hqqq.Contracts

Cross-service message contracts, Kafka event DTOs, and shared value types.

This project contains **no business logic** — only data shapes that flow between
services over Kafka, Redis, or REST boundaries.

## Contents

- `Events/` — Kafka event record types
  - `RawTickV1` — normalized market tick (`market.raw_ticks.v1`)
  - `LatestSymbolQuoteV1` — compacted latest quote (`market.latest_by_symbol.v1`)
  - `BasketActiveStateV1` — fully-materialized active basket (`refdata.basket.active.v1`)
  - `BasketActivatedV1` — slim basket lifecycle signal (reserved for `refdata.basket.events.v1`)
  - `QuoteSnapshotV1` — iNAV snapshot (`pricing.snapshots.v1`)
  - `IncidentEventV1` — operational incident (`ops.incidents.v1`)
- `Dtos/` — Shared REST / SignalR DTOs
  - `QuoteSnapshotDto` — serving shape for latest iNAV
  - `SeriesPointDto` — time-series data point
  - `MoverDto` — top mover / laggard
  - `FreshnessDto` — tick freshness summary
