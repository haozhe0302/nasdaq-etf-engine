# Phase 2 — Repository Restructure Notes

Completed: Phase 2 repo layout restructure.

---

## What was created

### Shared libraries (`src/building-blocks/`)

| Project | Purpose |
|---------|---------|
| `Hqqq.Contracts` | Cross-service Kafka event DTOs and shared value types |
| `Hqqq.Domain` | Pure domain model — entities, value objects, domain services |
| `Hqqq.Infrastructure` | Kafka/Redis/Timescale connection factories, serialization, hosting |
| `Hqqq.Observability` | Prometheus metrics, tracing, structured logging, health builders |

All contain placeholder namespace files only. Will be populated in Phase 2A1.

### Service skeletons (`src/services/`)

| Service | SDK | Port | Purpose |
|---------|-----|------|---------|
| `hqqq-reference-data` | Web | 5020 | Basket refresh, activation, corp-action adjustment |
| `hqqq-ingress` | Worker | — | Tiingo WS/REST normalization, Kafka publishing |
| `hqqq-quote-engine` | Worker | — | Kafka tick consumption, iNAV compute, Redis write |
| `hqqq-gateway` | Web | 5030 | REST + SignalR serving from Redis/Timescale |
| `hqqq-persistence` | Worker | — | Kafka-to-TimescaleDB writer |
| `hqqq-analytics` | Worker | — | Replay, backfill, anomaly detection |

All contain minimal `Program.cs` with health endpoint or background worker
skeleton. No business logic yet.

### Test project skeletons (`tests/`)

- `Hqqq.Contracts.Tests`
- `Hqqq.ReferenceData.Tests`
- `Hqqq.Ingress.Tests`
- `Hqqq.QuoteEngine.Tests`
- `Hqqq.Gateway.Tests`
- `Hqqq.Persistence.Tests`

Each contains a single smoke test asserting `true`.

### Build infrastructure

- `Hqqq.sln` — root solution file with solution folders: `building-blocks`,
  `services`, `tools`, `legacy`, `tests`
- `Directory.Build.props` — shared settings: `net10.0`, nullable, implicit usings

## What was moved

- `src/hqqq-bench` → `src/tools/hqqq-bench`
  - `ProjectReference` in `hqqq-bench.csproj` updated to `../../hqqq-api/hqqq-api.csproj`
  - `ProjectReference` in `hqqq-api.tests.csproj` updated to `../tools/hqqq-bench/hqqq-bench.csproj`

## What stayed as legacy

- `src/hqqq-api/` — Phase 1 modular monolith, fully preserved and compilable.
  Marked with legacy comments in `Program.cs` and `hqqq-api.csproj`.
- `src/hqqq-api.tests/` — Phase 1 test project, fully preserved and compilable.
  Marked with legacy comment in `hqqq-api.tests.csproj`.

The legacy monolith continues to serve as the running system until Phase 2B
completes the gateway cutover.

## What is only scaffolded

All new services (`src/services/*`) contain **placeholder implementations only**:
- Web services: `/healthz` endpoint returning `"ok"`
- Worker services: `BackgroundService` with a 5-second delay loop
- Gateway: placeholder `/api/quote` returning 503 "not yet wired", SignalR hub at `/hubs/market`

No Kafka, Redis, or TimescaleDB runtime code has been added yet.

## Legacy backup code blocks

None in this step. All legacy code remains in its original location under
`src/hqqq-api/Modules/`. Code will be migrated (not copied) in subsequent steps.

## Module-to-service mapping (planned)

| Current MVP module | Phase 2 target service |
|--------------------|------------------------|
| `Basket` | `hqqq-reference-data` |
| `CorporateActions` | `hqqq-reference-data` |
| `MarketData` | `hqqq-ingress` |
| `Pricing` | `hqqq-quote-engine` |
| `History` | `hqqq-persistence` + `hqqq-gateway` (query layer) |
| `Benchmark` | `hqqq-analytics` |
| `System` | `Hqqq.Observability` + `hqqq-gateway` (system endpoints) |
| `MarketHub` | `hqqq-gateway` |

## Next steps

- **Phase 2A1**: Populate `Hqqq.Contracts` and `Hqqq.Domain` with extracted
  types from current MVP modules
- **Phase 2A2**: Build `hqqq-reference-data` service with basket/corp-action logic
- **Phase 2A3**: Build `hqqq-ingress` service with Tiingo adapter
- **Phase 2A4**: Local infra bootstrap and Kafka topic/config plumbing
