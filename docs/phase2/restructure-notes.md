# Phase 2 — Repository Restructure Notes

Status: Phase 2 repo layout restructure + 2A hardening complete. Phase 2B
runtime work through B5 is in place (quote-engine real compute + Redis/Kafka
materialization; gateway layered source selection with Redis-backed
`/api/quote` and `/api/constituents`). History and system-health remain on the
B1 transitional path (stub or legacy forwarding) and are not yet cut over.

---

## What was created

### Shared libraries (`src/building-blocks/`)

| Project | Purpose | Status |
|---------|---------|--------|
| `Hqqq.Contracts` | Cross-service Kafka event DTOs and shared value types | Types defined |
| `Hqqq.Domain` | Pure domain model — entities, value objects, domain services | Types defined + pricing math (raw basket value, scale-factor, movers, freshness) used by quote-engine |
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
| `hqqq-reference-data` | Web | 5020 | Basket refresh, activation, corp-action adjustment | Partial — config binding + in-memory basket repository; issuer feeds / corp-action pipeline not yet implemented |
| `hqqq-ingress` | Worker | — | Tiingo WS/REST normalization, Kafka publishing | Stub with Tiingo + Kafka options |
| `hqqq-quote-engine` | Worker | — | Kafka tick consumption, iNAV compute, Redis write | **B4 live** — real Kafka consumers (`market.raw_ticks.v1`, `refdata.basket.active.v1`), iNAV compute, file checkpoint, Redis snapshot + constituents writers, `pricing.snapshots.v1` publish |
| `hqqq-gateway` | Web | 5030 | REST + SignalR serving from Redis/Timescale | **B1 + B5 live** — compatibility shell routes all four REST endpoints + `/hubs/market`; layered source selection (global `Gateway:DataSource` + per-endpoint `Gateway:Sources:*`); `/api/quote` and `/api/constituents` serve from Redis when configured; `/api/history` and `/api/system/health` stay on stub or legacy forwarding |
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

## Current Phase 2B state (as of B5)

Live in the new services:

- `hqqq-quote-engine`
  - Consumes `market.raw_ticks.v1` and the compacted `refdata.basket.active.v1`
    (`BasketActiveStateV1`) from Kafka; pure pricing math in `Hqqq.Domain`.
  - File-based engine checkpoint (basket identity + pricing basis + scale
    factor + last snapshot digest) restored before consumers start, so the
    previously-active basket is rehydrated before the first live message.
  - Writes the latest quote snapshot to Redis at `hqqq:snapshot:{basketId}`
    and the latest constituents snapshot to `hqqq:constituents:{basketId}`
    every materialize cycle.
  - Publishes `QuoteSnapshotV1` to `pricing.snapshots.v1` for downstream
    persistence / analytics consumers.
  - Sink failures are isolated per-sink; one failure never blocks the others
    or crashes the worker.

- `hqqq-gateway`
  - REST routes `GET /api/quote`, `GET /api/constituents`,
    `GET /api/history?range=`, `GET /api/system/health`, plus
    `/hubs/market` (SignalR) are all mapped.
  - Layered source selection: a global `Gateway:DataSource` (`stub` or
    `legacy`, empty = auto-detect in Development) acts as the fallback,
    and individual endpoints can be overridden via `Gateway:Sources:Quote`
    and `Gateway:Sources:Constituents`.
  - B5 Redis-backed serving for `/api/quote` and `/api/constituents`:
    reads the snapshots written by `hqqq-quote-engine`. Explicit error
    surface — missing key → HTTP 503 with
    `{"error":"quote_unavailable"|"constituents_unavailable", ...}`;
    malformed payload → HTTP 502 with
    `{"error":"quote_malformed"|"constituents_malformed"}`; no silent
    fallback to stub data.
  - `/api/history` and `/api/system/health` **intentionally** do not accept
    `redis` in B5 — they follow only the global `Gateway:DataSource` for
    now.
  - See [../../src/services/hqqq-gateway/README.md](../../src/services/hqqq-gateway/README.md)
    for the full B-phase operating-modes matrix.

Still stubbed / transitional in the new services:

- `hqqq-ingress` — no real Tiingo websocket/REST ingestion yet.
- `hqqq-persistence` — no Kafka-to-Timescale writer yet; `pricing.snapshots.v1`
  is not yet consumed into history storage.
- `hqqq-analytics` — no replay/backfill/anomaly jobs yet.
- `hqqq-reference-data` — in-memory basket repository only; real issuer
  feeds and corporate-action pipeline not wired.
- `hqqq-gateway` — `/api/history` and `/api/system/health` still return
  stub DTOs or forward to the legacy monolith depending on
  `Gateway:DataSource`; there is no native gateway aggregation or
  Timescale-backed reader yet.

Services start, bind configuration, report their posture, and idle when
their inputs are not wired. Dependencies report as "degraded" rather than
crashing when unavailable.

## Intentionally deferred

| Deferred item | Target |
|---|---|
| Timescale-backed `/api/history` in the gateway | Phase 2C3 (history cutover) |
| Gateway-native `/api/system/health` aggregation (instead of legacy forwarding) | Later observability step |
| Real Tiingo ingestion in `hqqq-ingress` | Phase 2B (ingress cutover) |
| Kafka-to-Timescale writer in `hqqq-persistence` | Phase 2C |
| Replay / backfill / anomaly in `hqqq-analytics` | Phase 2C |
| Issuer-feed + corporate-action pipeline in `hqqq-reference-data` | Phase 2B/C reference-data cutover |
| Redis pub/sub SignalR backplane on `/hubs/market` (multi-replica fan-out) | Phase 2D2 |
| Multi-replica / HA infra (gateway scale-out, consumer-group ownership) | Phase 2D3 |

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

- **Phase 2B** (remaining): Wire real Tiingo ingestion in `hqqq-ingress`;
  complete `hqqq-reference-data` issuer feeds + corporate-action adjustment.
- **Phase 2C**: Implement Kafka-to-Timescale persistence pipeline
  (`pricing.snapshots.v1` → Timescale); swap gateway `IHistorySource` to a
  Timescale-backed reader (C3 history cutover).
- **Later observability**: Replace gateway `/api/system/health` legacy
  forwarding with native aggregation over shared observability health
  payloads.
- **Phase 2C**: Add replay, backfill, and anomaly detection in
  `hqqq-analytics`.
- **Phase 2D2**: Redis pub/sub SignalR backplane for `/hubs/market`
  multi-instance fan-out.
- **Phase 2D3**: Multi-replica / HA deployment topology.
