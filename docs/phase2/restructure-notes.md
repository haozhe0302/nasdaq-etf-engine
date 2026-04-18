# Phase 2 — Repository Restructure Notes

Status: Phase 2 repo layout restructure + 2A hardening + 2B through B5 +
2C through C4 are in place. The quote-engine does real compute and
Redis/Kafka materialization; the gateway has layered source selection with
Redis-backed `/api/quote`/`/api/constituents` and a Timescale-backed
`/api/history`; the persistence service consumes
`pricing.snapshots.v1` and `market.raw_ticks.v1` into TimescaleDB with
rollups + retention; `hqqq-analytics` runs a one-shot report over
persisted Timescale data. Gateway-native `/api/system/health` aggregation,
SignalR Redis backplane, and multi-replica HA remain deferred.

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
| `hqqq-gateway` | Web | 5030 | REST + SignalR serving from Redis/Timescale | **B1 + B5 + C2 live** — compatibility shell routes all four REST endpoints + `/hubs/market`; layered source selection (global `Gateway:DataSource` + per-endpoint `Gateway:Sources:*`); `/api/quote` and `/api/constituents` serve from Redis when configured; `/api/history` serves from Timescale when `Gateway:Sources:History=timescale`; `/api/system/health` still follows the global stub/legacy switch |
| `hqqq-persistence` | Worker | — | Kafka-to-TimescaleDB writer | **C3 live** — consumes `pricing.snapshots.v1` + `market.raw_ticks.v1`; idempotent schema / rollup / retention bootstrap at startup; batched transactional writes with `ON CONFLICT DO NOTHING` |
| `hqqq-analytics` | Worker | — | Replay, backfill, anomaly detection | **C4 one-shot report mode** — reads `quote_snapshots` (optionally `raw_ticks` aggregate) from Timescale for a requested basket + UTC window, computes a deterministic quality / tracking summary, optionally emits JSON, exits cleanly. Replay / backfill / anomaly remain deferred. |

All services now:
- Bind shared infrastructure options via `AddHqqqKafka`/`AddHqqqRedis`/`AddHqqqTimescale`
- Register shared observability via `AddHqqqObservability`
- Log configuration posture on startup
- Include legacy config fallback via `LegacyConfigShim`
- Web services expose `/healthz/live` and `/healthz/ready` with JSON payloads
- Worker services log dependency configuration at startup and run real
  Kafka consumers / writers / report jobs where implemented (quote-engine,
  persistence, analytics) or idle loops where still stubbed (ingress)

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

The legacy monolith continues to serve as the running reference system
while Phase 2 services take over responsibilities in narrow, verifiable
slices. The legacy API is still the only source of real Tiingo ingestion,
basket refresh, corporate-action adjustment, and `/api/system/health`
aggregation today.

## Current Phase 2 state (through C4)

Live in the new services:

- `hqqq-quote-engine` (B4)
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

- `hqqq-gateway` (B5 + C2)
  - REST routes `GET /api/quote`, `GET /api/constituents`,
    `GET /api/history?range=`, `GET /api/system/health`, plus
    `/hubs/market` (SignalR) are all mapped.
  - Layered source selection: a global `Gateway:DataSource` (`stub` or
    `legacy`, empty = auto-detect in Development) acts as the fallback,
    and individual endpoints can be overridden via `Gateway:Sources:Quote`,
    `Gateway:Sources:Constituents`, and `Gateway:Sources:History`.
  - **B5 Redis-backed** serving for `/api/quote` and `/api/constituents`:
    reads the snapshots written by `hqqq-quote-engine`. Missing key →
    HTTP 503 with `{"error":"quote_unavailable"|"constituents_unavailable", ...}`;
    malformed payload → HTTP 502 with
    `{"error":"quote_malformed"|"constituents_malformed"}`; no silent
    fallback to stub data.
  - **C2 Timescale-backed** `/api/history`: reads `quote_snapshots` directly
    and composes the existing frontend response contract
    (`range, startDate, endDate, pointCount, totalPoints, isPartial,
    series[time,nav,marketPrice], trackingError, distribution, diagnostics`).
    Unsupported `range` → HTTP 400; empty window → HTTP 200 with a
    render-safe empty payload; Timescale query failure → HTTP 503 with
    `{"error":"history_unavailable",...}`.
  - `/api/system/health` still follows only the global `Gateway:DataSource`
    (stub or legacy forwarding).
  - See [../../src/services/hqqq-gateway/README.md](../../src/services/hqqq-gateway/README.md)
    for the full B/C operating-modes matrix.

- `hqqq-persistence` (C3)
  - Consumes `pricing.snapshots.v1` (group `persistence-snapshots`) into
    the `quote_snapshots` hypertable.
  - Consumes `market.raw_ticks.v1` (group `persistence-raw-ticks`) into
    the `raw_ticks` hypertable.
  - Idempotent DDL at startup for both hypertables, `quote_snapshots_1m`
    and `quote_snapshots_5m` continuous aggregates, and
    `add_retention_policy` on all four. Toggle via
    `Persistence:SchemaBootstrapOnStart`.
  - Batched, transactional inserts with replay-safe
    `ON CONFLICT ... DO NOTHING` on `(basket_id, ts)` for snapshots and
    `(symbol, provider_timestamp, sequence)` for raw ticks.
  - Raw-tick and snapshot pipelines are fully isolated; one write failure
    never blocks the other.

- `hqqq-analytics` (C4)
  - One-shot `Analytics:Mode=report` job: reads `quote_snapshots` (and,
    when opted in, a cheap `raw_ticks` count) for a requested basket +
    UTC window from Timescale, computes a deterministic quality /
    tracking summary, optionally writes a JSON artifact, then stops the
    host cleanly.
  - Does not run on the hot path: no Kafka consumption, no Redis, no
    HTTP calls into other services, no schema ownership.
  - Empty-window runs produce `hasData=false` with zeroed numeric fields
    and exit successfully (exit code 0); replay / anomaly / backfill
    remain deferred seams only.

Still stubbed / transitional in the new services:

- `hqqq-ingress` — no real Tiingo websocket/REST ingestion yet; Tiingo
  ingestion still happens inside the legacy monolith.
- `hqqq-reference-data` — in-memory basket repository only; real issuer
  feeds and corporate-action pipeline still live in the legacy monolith.
- `hqqq-gateway` — `/api/system/health` still returns stub DTOs or
  forwards to the legacy monolith depending on `Gateway:DataSource`;
  there is no native gateway aggregation yet.
- `/hubs/market` uses plain `AddSignalR()` only; there is no Redis
  backplane yet (single-replica serving only).

Services start, bind configuration, report their posture, and consume
or idle appropriately. Dependencies report as "degraded" rather than
crashing when unavailable.

## Intentionally deferred

| Deferred item | Target |
|---|---|
| Gateway-native `/api/system/health` aggregation (instead of legacy forwarding) | Later observability step |
| Real Tiingo ingestion in `hqqq-ingress` | Phase 2B (ingress cutover, remaining) |
| Issuer-feed + corporate-action pipeline in `hqqq-reference-data` | Phase 2B/C reference-data cutover |
| Replay / backfill / anomaly detection in `hqqq-analytics` | Phase 2C5+ / 2D |
| `constituent_snapshots` / `basket_versions` persistence tables | Phase 2C5+ |
| Re-pointing the gateway `/api/history` read-side at the 1m/5m rollups | Phase 2C5+ |
| Redis pub/sub SignalR backplane on `/hubs/market` (multi-replica fan-out) | Phase 2D2 |
| Multi-replica / HA infra (gateway scale-out, consumer-group ownership) | Phase 2D3 |
| Kubernetes app-tier deployments | Phase 3 |

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
- **Phase 2C5+**: Add replay, backfill, and anomaly detection in
  `hqqq-analytics`; introduce `constituent_snapshots` / `basket_versions`;
  optionally re-point gateway `/api/history` at the 1m/5m continuous
  aggregates for long-range queries.
- **Later observability**: Replace gateway `/api/system/health` legacy
  forwarding with native aggregation over shared observability health
  payloads.
- **Phase 2D2**: Redis pub/sub SignalR backplane for `/hubs/market`
  multi-instance fan-out.
- **Phase 2D3**: Multi-replica / HA deployment topology.
- **Phase 3**: Kubernetes app-tier operationalization.
