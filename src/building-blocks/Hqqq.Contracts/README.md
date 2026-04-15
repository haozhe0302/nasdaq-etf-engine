# Hqqq.Contracts

Cross-service message contracts, Kafka event DTOs, and shared value types.

This project contains **no business logic** — only data shapes that flow between
services over Kafka, Redis, or REST boundaries.

## Planned contents (Phase 2)

- `Events/` — Kafka event record types (`RawTickV1`, `BasketActivatedV1`, `QuoteSnapshotV1`, etc.)
- `Dtos/` — Shared REST/SignalR DTOs (`QuoteSnapshotDto`, `ConstituentsDto`, etc.)
