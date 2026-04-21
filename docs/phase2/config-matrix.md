# Phase 2 — Deployment Configuration Matrix

Per-service config surface for the Phase 2 app tier. This is the
single grep target for "what env vars does service X actually need,
on what port does it listen, and what does it depend on?".

Conventions:
- All services use .NET hierarchical configuration with `__` as the
  section separator (e.g. `Tiingo__ApiKey` →
  `{ "Tiingo": { "ApiKey": "..." } }`).
- "Required" means the service either fails fast on startup
  (`IValidateOptions`) or cannot serve its primary endpoint without
  the value.
- "Optional" means a sensible default exists.
- "Set in" columns: ` ` = .env example, `C` = `docker-compose.phase2.yml`,
  `B` = `infra/azure/main.bicep`. A blank cell means the value is not
  set in that surface (and the default applies).

Cross-references:
- [`.env.example`](../../.env.example)
- [`docker-compose.phase2.yml`](../../docker-compose.phase2.yml)
- [`infra/azure/main.bicep`](../../infra/azure/main.bicep)

---

## Shared infra (consumed by multiple services)

| Section | Key | Where |
|---------|-----|-------|
| Operating mode | `OperatingMode` (canonical) or `HQQQ_OPERATING_MODE` (legacy flat alias) | env / `C` / `B` |
| Kafka | `Kafka__BootstrapServers`, `Kafka__ClientId`, `Kafka__ConsumerGroupPrefix` | env / `C` / `B` |
| Kafka (auth, optional) | `Kafka__SecurityProtocol`, `Kafka__SaslMechanism`, `Kafka__SaslUsername`, `Kafka__SaslPassword`, `Kafka__EnableTopicBootstrap` | env (operator-set) |
| Redis | `Redis__Configuration` | env / `C` / `B` |
| Timescale | `Timescale__ConnectionString` | env / `C` / `B` |

> **Operating mode (Phase 2).** `OperatingMode=hybrid` (default) keeps
> `hqqq-ingress` and `hqqq-reference-data` as stubs so the legacy
> `hqqq-api` monolith remains the source of truth for live ticks and
> the active basket. `OperatingMode=standalone` activates the Phase 2
> native paths: `hqqq-ingress` consumes the Tiingo IEX websocket and
> publishes `market.raw_ticks.v1` + `market.latest_by_symbol.v1`, and
> `hqqq-reference-data` loads a deterministic basket seed and publishes
> `refdata.basket.active.v1`. In standalone mode services fail fast
> when their required native dependencies are missing (e.g. a Tiingo
> API key for ingress, a parseable seed for reference-data). See
> [`docs/phase2/azure-deploy.md` §0.2](azure-deploy.md) for the
> hybrid-vs-standalone decision matrix.

> **Kafka auth posture.** When `Kafka__SecurityProtocol` is unset (local
> docker-compose), every producer/consumer/admin client connects
> Plaintext exactly as before — defaults are byte-identical. When
> targeting **Azure Event Hubs Kafka**, set all five of:
> `Kafka__SecurityProtocol=SaslSsl`, `Kafka__SaslMechanism=Plain`,
> `Kafka__SaslUsername=$ConnectionString`,
> `Kafka__SaslPassword=<Event Hubs namespace connection string>`, and
> `Kafka__EnableTopicBootstrap=false`. Topics must be pre-provisioned
> on the Event Hubs namespace; with `EnableTopicBootstrap=false` the
> shared bootstrap helper switches to metadata-only validation and
> warns (does not crash) when an expected topic is missing. Bicep /
> GitHub Actions plumbing for these secrets is a follow-up diff —
> today operators set them on the Container App env directly.

---

## `hqqq-gateway` (web)

REST + SignalR serving edge. External-facing on Azure (the only Phase 2
service with `ingress.external=true`).

| Env var | Required? | Default | Purpose |
|---------|-----------|---------|---------|
| `ASPNETCORE_URLS` | yes (containers) | `http://+:8080` (B) | Bind address. |
| `Redis__Configuration` | yes (when any source = `redis`) | — | Snapshot reads + pub/sub subscribe to `hqqq:channel:quote-update`. |
| `Timescale__ConnectionString` | yes (when `Sources:History=timescale`) | — | History reads from `quote_snapshots`. |
| `Gateway__BasketId` | yes | `HQQQ` | Used to format Redis keys (`hqqq:snapshot:{basketId}` / `hqqq:constituents:{basketId}`). |
| `Gateway__DataSource` | optional | empty (auto-detect: `legacy` if `LegacyBaseUrl` is set in Development; otherwise `stub`) | Global fallback for any endpoint without an override. Allowed: `stub`, `legacy`. |
| `Gateway__LegacyBaseUrl` | conditional | — | Required when any endpoint resolves to `legacy`. |
| `Gateway__Sources__Quote` | optional | inherits `Gateway:DataSource`; compose/B sets `redis` | Per-endpoint override. Allowed: `stub`, `legacy`, `redis`. |
| `Gateway__Sources__Constituents` | optional | inherits; compose/B sets `redis` | Per-endpoint override. Allowed: `stub`, `legacy`, `redis`. |
| `Gateway__Sources__History` | optional | inherits (stub/legacy only); compose/B sets `timescale` | Per-endpoint override. Allowed: `stub`, `legacy`, `timescale`. |
| `Gateway__Sources__SystemHealth` | optional | `aggregated` (D1 default) | Per-endpoint override. Allowed: `aggregated`, `legacy`, `stub`. |
| `Gateway__Health__RequestTimeoutSeconds` | optional | `1.5` | Per-call timeout for each downstream `/healthz/ready` probe. |
| `Gateway__Health__IncludeRedis` | optional | `true` | Include local Redis check in `/api/system/health`. |
| `Gateway__Health__IncludeTimescale` | optional | `true` | Include local Timescale check in `/api/system/health`. |
| `Gateway__Health__Services__ReferenceData__BaseUrl` | optional | empty → `idle` | Where the aggregator scrapes `/healthz/ready`. |
| `Gateway__Health__Services__Ingress__BaseUrl` | optional | empty → `idle` | Same. |
| `Gateway__Health__Services__QuoteEngine__BaseUrl` | optional | empty → `idle` | Same. |
| `Gateway__Health__Services__Persistence__BaseUrl` | optional | empty → `idle` | Same. |
| `Gateway__Health__Services__Analytics__BaseUrl` | optional | empty → `idle` | Same. |

| Surface | Container DNS:port | External port |
|---------|--------------------|---------------|
| Host (`dotnet run`) | `localhost:5030` | `5030` |
| Containerized (D3) | `hqqq-gateway:8080` | `5030` (host) |
| Replica-smoke (D5) | `hqqq-gateway:8080` + `hqqq-gateway-b:8080` | `5030`, `5031` |
| Azure Container Apps (D4) | `<gatewayFqdn>:443` (HTTPS) | external ingress |

Dependencies:
- **Reads** Redis keys `hqqq:snapshot:{basketId}`, `hqqq:constituents:{basketId}` (when sources are `redis`).
- **Subscribes** to Redis pub/sub `hqqq:channel:quote-update`.
- **Reads** Timescale `quote_snapshots` (when `Sources:History=timescale`).
- **HTTP** to each downstream worker's management `/healthz/ready` (when configured).

---

## `hqqq-quote-engine` (worker)

Real Kafka consumer + iNAV compute. Singleton today (one replica) so
checkpoint state stays coherent.

| Env var | Required? | Default | Purpose |
|---------|-----------|---------|---------|
| `Kafka__BootstrapServers` | yes | — | Consume `market.raw_ticks.v1`, `refdata.basket.active.v1`; produce `pricing.snapshots.v1`. |
| `Kafka__ClientId` | optional | `hqqq-local` (env), `hqqq-azure` (B) | Producer/consumer client id. |
| `Kafka__ConsumerGroupPrefix` | optional | `hqqq` | Prefix for derived consumer-group names. |
| `Redis__Configuration` | yes | — | Write snapshot/constituents keys; publish to `hqqq:channel:quote-update`. |
| `QuoteEngine__CheckpointPath` | optional | `./data/quote-engine/checkpoint.json` (env), `/data/quote-engine/checkpoint.json` (C, on `quote_engine_data` named volume), `/mnt/quote-engine/checkpoint.json` (B when `quoteEngineCheckpointPersistence=true`, on Azure Files mount), `/tmp/quote-engine/checkpoint.json` (B when persistence is off, ephemeral) | Crash-recovery checkpoint location. **Must** map to a persistent volume in containers. On Azure the bicepparam toggle `quoteEngineCheckpointPersistence` provisions an Azure Files share + mount and points this env var at it; with the toggle off the path falls back to ephemeral `/tmp` and the checkpoint is lost on revision swap. See [`docs/phase2/azure-deploy.md` §9](azure-deploy.md). |
| `QuoteEngine__CheckpointInterval` | optional | `00:00:10` | How often to flush the checkpoint to disk. |
| `Management__Enabled` | yes (containers) | `true` (C/B) | Enables the management host on `Management__Port` for `/healthz/*` and `/metrics`. |
| `Management__Port` | yes (containers) | `8081` (C/B) | Management host port. |
| `Management__BindAddress` | yes (containers) | `0.0.0.0` (C/B) | Required so the gateway aggregator can reach the management endpoints across the docker network / CAE. |

| Surface | Container DNS:port | External port |
|---------|--------------------|---------------|
| Host (`dotnet run`) | management on `localhost:<mgmtPort>` | management only |
| Containerized (D3) | `hqqq-quote-engine:8081` | `5082` (host) |
| Azure Container Apps (D4) | `<quoteEngineInternalFqdn>` (internal-only) | none (internal ingress) |

Dependencies:
- **Consumes** Kafka topics `market.raw_ticks.v1`, `refdata.basket.active.v1`.
- **Produces** Kafka topic `pricing.snapshots.v1`.
- **Writes** Redis keys `hqqq:snapshot:{basketId}`, `hqqq:constituents:{basketId}`, `hqqq:freshness:{basketId}`.
- **Publishes** Redis channel `hqqq:channel:quote-update`.

---

## `hqqq-persistence` (worker)

Kafka → TimescaleDB writer. Idempotent schema bootstrap on first start.

| Env var | Required? | Default | Purpose |
|---------|-----------|---------|---------|
| `Kafka__BootstrapServers` | yes | — | Consume `pricing.snapshots.v1` (group `persistence-snapshots`) and `market.raw_ticks.v1` (group `persistence-raw-ticks`). |
| `Timescale__ConnectionString` | yes | — | Write `quote_snapshots` and `raw_ticks` hypertables. |
| `Persistence__SchemaBootstrapOnStart` | optional | `true` (env/C/B) | Idempotent DDL: hypertables, `quote_snapshots_1m`/`5m` continuous aggregates, retention policies. Disable only when schema is owned externally. |
| `Persistence__SnapshotWriteBatchSize` | optional | `128` | Snapshot insert batch size. |
| `Persistence__RawTickWriteBatchSize` | optional | `256` | Raw-tick insert batch size. |
| `Persistence__RawTickRetention` | optional | `30.00:00:00` | `add_retention_policy` on `raw_ticks`; `.NET TimeSpan` format. |
| `Persistence__QuoteSnapshotRetention` | optional | `365.00:00:00` | `add_retention_policy` on `quote_snapshots`. |
| `Persistence__RollupRetention` | optional | `730.00:00:00` | `add_retention_policy` on `quote_snapshots_1m` / `quote_snapshots_5m`. |
| `Management__*` | yes (containers) | as for quote-engine | Management host. |

| Surface | Container DNS:port | External port |
|---------|--------------------|---------------|
| Host (`dotnet run`) | management on `localhost:<mgmtPort>` | management only |
| Containerized (D3) | `hqqq-persistence:8081` | `5083` (host) |
| Azure Container Apps (D4) | `<persistenceInternalFqdn>` (internal-only) | none |

Dependencies:
- **Consumes** Kafka topics `pricing.snapshots.v1`, `market.raw_ticks.v1`.
- **Writes** Timescale tables `quote_snapshots`, `raw_ticks` and the
  `quote_snapshots_1m` / `quote_snapshots_5m` continuous aggregates.

---

## `hqqq-reference-data` (web)

Basket registry. **Owns the active basket in both operating modes** —
behaviour is identical in `hybrid` and `standalone`; basket ownership no
longer varies by mode. The service runs a composite holdings pipeline:
live source first (config-driven file or HTTP drop), with a committed
~100-name deterministic fallback seed as the safety net. It publishes
the fully-materialized `BasketActiveStateV1` payload (including every
constituent) to `refdata.basket.active.v1` on change and on a slow
re-publish cadence so late consumers hydrate without operator action.

| Env var | Required? | Default | Purpose |
|---------|-----------|---------|---------|
| `ASPNETCORE_URLS` | yes (containers) | `http://+:8080` (B) | Bind address. |
| `OperatingMode` | optional | `hybrid` | `hybrid` or `standalone`. Inherited from the shared `HQQQ_OPERATING_MODE` (env / C / B). Affects cross-cutting posture only — does not change basket ownership. |
| `Kafka__BootstrapServers` | required | — | Producer for `refdata.basket.active.v1`. |
| `ReferenceData__SeedPath` | optional | empty (use embedded seed) | Filesystem override for the deterministic fallback seed. Unset → embedded resource. |
| `ReferenceData__LiveHoldings__SourceType` | optional | `None` | `None` / `File` / `Http`. `None` is the demo posture — composite goes straight to the fallback seed. |
| `ReferenceData__LiveHoldings__FilePath` | optional | empty | Used when `SourceType=File`. |
| `ReferenceData__LiveHoldings__HttpUrl` | optional | empty | Used when `SourceType=Http`. |
| `ReferenceData__LiveHoldings__HttpTimeoutSeconds` | optional | `10` | HTTP per-request timeout for the live source. |
| `ReferenceData__LiveHoldings__StaleAfterHours` | optional | `0` | `0` disables the `asOfDate`-based staleness check. |
| `ReferenceData__Refresh__IntervalSeconds` | optional | `600` | Periodic refresh cadence. `0` disables the timer (startup-only). |
| `ReferenceData__Refresh__RepublishIntervalSeconds` | optional | `300` | Slow re-publish so late consumers hydrate. `0` disables. |
| `ReferenceData__Refresh__StartupMaxWaitSeconds` | optional | `30` | Upper bound on the startup refresh attempt. |
| `ReferenceData__Validation__Strict` | optional | `true` | Strict: any validator error → fall back to seed. Permissive: tolerate per-row issues. |
| `ReferenceData__Validation__MinConstituents` | optional | `50` | Soft lower bound on constituent count. |
| `ReferenceData__Validation__MaxConstituents` | optional | `150` | Soft upper bound (guards duplicated feeds). |
| `ReferenceData__Publish__TopicName` | optional | empty (`refdata.basket.active.v1`) | Override topic name. |
| `ReferenceData__PublishHealth__FirstActivationGraceSeconds` | optional | `60` | Grace window after first activation before a never-published basket degrades readiness. |
| `ReferenceData__PublishHealth__DegradedAfterConsecutiveFailures` | optional | `1` | Consecutive publish failures before `/healthz/ready` flips to Degraded (503). |
| `ReferenceData__PublishHealth__UnhealthyAfterConsecutiveFailures` | optional | `5` | Consecutive publish failures before `/healthz/ready` flips to Unhealthy (503). |
| `ReferenceData__PublishHealth__MaxSilenceSeconds` | optional | `900` | Maximum tolerated silence between successful publishes before Unhealthy. |
| `Redis__Configuration` | optional today | — | Reserved for future basket-state mirror. |
| `Timescale__ConnectionString` | optional today | — | Reserved for future basket persistence. |

| Surface | Container DNS:port | External port |
|---------|--------------------|---------------|
| Host (`dotnet run`) | `localhost:5020` | `5020` |
| Containerized (D3) | `hqqq-reference-data:8080` | `5020` (host) |
| Azure Container Apps (D4) | `<referenceDataInternalFqdn>` (internal-only) | none |

Dependencies:
- Produces to Kafka topic `refdata.basket.active.v1` (compacted,
  key = basket id) — **full constituent payload**, never header-only.
- The readiness probe (`active-basket`) is `Unhealthy` until the first
  successful refresh activates a basket; afterwards it exposes
  basketId, version, as-of date, fingerprint, constituent count, and
  lineage source (e.g. `live:file`, `live:http`, `fallback-seed`).
- The gateway aggregator scrapes `/healthz/ready`.

---

## `hqqq-ingress` (worker)

Tiingo ingest worker. Behaviour depends on `OperatingMode`:
- **Hybrid** (default): no-op stub. The legacy `hqqq-api` monolith
  bridges live ticks. If `Tiingo__ApiKey` is set in hybrid mode the
  worker logs a single warning and ignores the key (avoids
  double-publish / split-brain with the monolith).
- **Standalone**: connects to the Tiingo IEX websocket, runs an
  optional REST snapshot warmup so consumers see a baseline price
  before the first live tick, and publishes both `market.raw_ticks.v1`
  (time-series) and `market.latest_by_symbol.v1` (compacted, key =
  symbol) for every observed tick. Reconnects with bounded
  exponential backoff. Fails fast at startup if `Tiingo__ApiKey` is
  missing or a placeholder.

| Env var | Required? | Default | Purpose |
|---------|-----------|---------|---------|
| `OperatingMode` | optional | `hybrid` | Inherited from `HQQQ_OPERATING_MODE`. Selects stub-vs-live behaviour. |
| `Tiingo__ApiKey` | **required in standalone**, ignored in hybrid | empty | Personal Tiingo IEX API key. Standalone fail-fast also rejects placeholder values like `your_api_key`, `<set tiingo api key>`, `changeme`. |
| `Tiingo__WsUrl` | optional | `wss://api.tiingo.com/iex` | Tiingo IEX WebSocket. |
| `Tiingo__RestBaseUrl` | optional | `https://api.tiingo.com/iex` | Tiingo IEX REST (snapshot warmup). |
| `Tiingo__Symbols` | optional | empty → falls back to a top-25 default that mirrors the standalone basket seed | Comma/semicolon-separated subscription override. |
| `Tiingo__SnapshotOnStartup` | optional | `true` | Disable the REST warmup (e.g. in tests). |
| `Tiingo__ReconnectBaseDelaySeconds` | optional | `5` | Initial websocket reconnect delay. |
| `Tiingo__MaxReconnectDelaySeconds` | optional | `60` | Reconnect backoff cap. |
| `Tiingo__StaleAfterSeconds` | optional | `60` | If no tick is observed for this long, `/healthz/ready` reports `Degraded`. |
| `Tiingo__WebSocketThresholdLevel` | optional | `6` | Tiingo IEX threshold (matches the legacy monolith / free tier). |
| `Kafka__BootstrapServers` | required in standalone | — | Producer for `market.raw_ticks.v1` and `market.latest_by_symbol.v1`. |
| `Management__*` | yes (containers) | as for quote-engine | Management host. |

| Surface | Container DNS:port | External port |
|---------|--------------------|---------------|
| Host (`dotnet run`) | management on `localhost:<mgmtPort>` | management only |
| Containerized (D3) | `hqqq-ingress:8081` | `5081` (host) |
| Azure Container Apps (D4) | `<ingressInternalFqdn>` (internal-only) | none |

Dependencies:
- **Hybrid**: none on the data plane (stub).
- **Standalone**: outbound HTTPS to Tiingo (REST + WSS) and produces
  to Kafka topics `market.raw_ticks.v1` (key = symbol) and
  `market.latest_by_symbol.v1` (compacted, key = symbol). Health
  surface (`ingress-upstream`) reports `isUpstreamConnected`,
  `ticksIngested`, `lastDataUtc`, and `lastError`/`lastErrorAtUtc`.

---

## `hqqq-analytics` (one-shot job)

Manual-trigger one-shot Timescale report. Not a long-running service —
it reads, summarizes, optionally emits a JSON artifact, and exits.

| Env var | Required? | Default | Purpose |
|---------|-----------|---------|---------|
| `Timescale__ConnectionString` | yes | — | Read `quote_snapshots` (and `raw_ticks` count when opted in). |
| `Analytics__Mode` | yes | `report` (env/C/B) | Only `report` is supported; any other value exits with code `2`. |
| `Analytics__BasketId` | yes | `HQQQ` | Basket scope for the report. |
| `Analytics__StartUtc` | yes | — | UTC start of the report window. **Must be set per execution** in Azure (Bicep deliberately does not bake this in). |
| `Analytics__EndUtc` | yes | — | UTC end of the report window. |
| `Analytics__EmitJsonPath` | optional | empty | Write a JSON artifact (mounted to `/artifacts` in compose; not mounted in Azure today). |
| `Analytics__IncludeRawTickAggregates` | optional | `false` | Adds a cheap `raw_ticks` count to the report. |

| Surface | Container DNS:port | External port |
|---------|--------------------|---------------|
| Host (`dotnet run`) | management on `localhost:<mgmtPort>` (when long-running invocation; usually not) | none |
| Containerized (D3, profile=`analytics`) | `hqqq-analytics:8081` | `5084` (host, when running) |
| Azure Container Apps (D4) | Manual-trigger Job (`Microsoft.App/jobs`) | none |

Exit codes: `0` success (incl. empty window), `1` failure, `2`
unsupported `Analytics:Mode`.

Dependencies:
- **Reads** Timescale tables `quote_snapshots` (and optionally `raw_ticks`).
- No Kafka, no Redis, no HTTP into other services, no schema ownership.

---

## Public vs internal port summary

| Service | Containerized (D3) host port | Azure (D4) ingress |
|---------|------------------------------|--------------------|
| `hqqq-gateway` | `5030` | **external** (HTTPS via CAE) |
| `hqqq-gateway-b` (D5 only) | `5031` | n/a (D5 is local-only) |
| `hqqq-reference-data` | `5020` | internal |
| `hqqq-ingress` | `5081` | internal |
| `hqqq-quote-engine` | `5082` | internal |
| `hqqq-persistence` | `5083` | internal |
| `hqqq-analytics` (profile) | `5084` | none (Manual job) |

The gateway is the only Phase 2 service exposed externally on Azure.
Workers are reachable inside the Container Apps environment via their
internal FQDNs, which is what the gateway's
`Gateway:Health:Services:*:BaseUrl` aggregator uses for
`/api/system/health`.
