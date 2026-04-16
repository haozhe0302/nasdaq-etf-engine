# Phase 2 — Repository Restructure Notes

Completed: Phase 2 repo layout restructure + Phase 2A cleanup/hardening.

---

## What was created

### Shared libraries (`src/building-blocks/`)

| Project | Purpose | Status |
|---------|---------|--------|
| `Hqqq.Contracts` | Cross-service Kafka event DTOs and shared value types | Types defined |
| `Hqqq.Domain` | Pure domain model — entities, value objects, domain services | Types defined |
| `Hqqq.Infrastructure` | Kafka/Redis/Timescale connection helpers, serialization, hosting, health checks | Implemented (lightweight) |
| `Hqqq.Observability` | Metrics definitions, tracing, structured logging, health payload builders | Implemented (scaffolding) |

`Hqqq.Infrastructure` contains:
- Kafka options, topic registry, bootstrap helper, producer/consumer config builders
- Redis options, key registry, connection factory
- Timescale options, connection factory
- Shared JSON serializer defaults
- Service registration extensions and legacy config shim
- Dependency health checks (degraded-not-crashed pattern)

`Hqqq.Observability` contains:
- Metric name constants and shared meter instruments
- Structured logging configuration extensions
- Activity source for tracing
- Health payload builder for consistent JSON responses

### Service skeletons (`src/services/`)

| Service | SDK | Port | Purpose | Status |
|---------|-----|------|---------|--------|
| `hqqq-reference-data` | Web | 5020 | Basket refresh, activation, corp-action adjustment | Stub with shared config binding |
| `hqqq-ingress` | Worker | — | Tiingo WS/REST normalization, Kafka publishing | Stub with Tiingo + Kafka options |
| `hqqq-quote-engine` | Worker | — | Kafka tick consumption, iNAV compute, Redis write | Stub with Kafka + Redis options |
| `hqqq-gateway` | Web | 5030 | REST + SignalR serving from Redis/Timescale | Stub with health endpoints |
| `hqqq-persistence` | Worker | — | Kafka-to-TimescaleDB writer | Stub with Kafka + Timescale options |
| `hqqq-analytics` | Worker | — | Replay, backfill, anomaly detection | Stub with Kafka + Timescale options |

All services now:
- Bind shared infrastructure options via `AddHqqqKafka`/`AddHqqqRedis`/`AddHqqqTimescale`
- Register shared observability via `AddHqqqObservability`
- Log configuration posture on startup
- Include legacy config fallback via `LegacyConfigShim`
- Web services expose `/healthz/live` and `/healthz/ready` with JSON payloads
- Worker services log dependency configuration at startup and run idle loops
- Contain explicit TODO markers for Phase 2B/C wiring points

### Test project skeletons (`tests/`)

- `Hqqq.Contracts.Tests`
- `Hqqq.ReferenceData.Tests`
- `Hqqq.Ingress.Tests`
- `Hqqq.QuoteEngine.Tests`
- `Hqqq.Gateway.Tests`
- `Hqqq.Persistence.Tests`

Each contains focused smoke tests for config binding, serialization, topic
metadata, and service startup where applicable.

### Build infrastructure

- `Hqqq.sln` — root solution file with solution folders: `building-blocks`,
  `services`, `tools`, `legacy`, `tests`
- `Directory.Build.props` — shared settings: `net10.0`, nullable, implicit usings
- `.github/workflows/phase2-ci.yml` — root solution smoke CI (build + test)

### Configuration

- `.env.example` — Phase 2 hierarchical configuration template
  (`Tiingo__ApiKey`, `Kafka__BootstrapServers`, `Redis__Configuration`, `Timescale__ConnectionString`)
- Legacy flat env vars documented as deprecated with migration guidance

### Local infrastructure

- `docker-compose.yml` — TimescaleDB, Redis, Kafka (KRaft), Kafka UI, Prometheus, Grafana
- Kafka auto topic creation disabled — topics bootstrapped explicitly
- `scripts/bootstrap-kafka-topics.{ps1,sh}` — idempotent topic creation scripts

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

## What is intentionally stubbed (Phase 2B/C)

All new services contain **placeholder implementations** with explicit TODO markers:
- No real Tiingo websocket/REST ingestion (hqqq-ingress)
- No real Kafka tick publishing/consumption
- No real iNAV quote calculation (hqqq-quote-engine)
- No Redis materialized snapshot serving (hqqq-gateway)
- No Timescale persistence logic (hqqq-persistence)
- No replay/backfill/anomaly jobs (hqqq-analytics)

Services start, bind configuration, report their posture, and idle.
Dependencies report as "degraded" rather than crashing when unavailable.

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

- **Phase 2B**: Wire real Tiingo ingestion in `hqqq-ingress`
- **Phase 2B**: Wire Kafka consumers/producers across services
- **Phase 2B**: Implement iNAV quote engine computation
- **Phase 2B**: Implement Redis snapshot writes and gateway serving
- **Phase 2B**: Implement Timescale persistence pipeline
- **Phase 2B**: Complete gateway cutover from legacy API
- **Phase 2C**: Add replay, backfill, and anomaly detection in analytics
