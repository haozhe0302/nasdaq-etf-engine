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
| Kafka | `Kafka__BootstrapServers`, `Kafka__ClientId`, `Kafka__ConsumerGroupPrefix` | env / `C` / `B` |
| Kafka (auth, optional) | `Kafka__SecurityProtocol`, `Kafka__SaslMechanism`, `Kafka__SaslUsername`, `Kafka__SaslPassword`, `Kafka__EnableTopicBootstrap` | env (operator-set) |
| Redis | `Redis__Configuration` | env / `C` / `B` |
| Timescale | `Timescale__ConnectionString` | env / `C` / `B` |

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
| `QuoteEngine__CheckpointPath` | optional | `./data/quote-engine/checkpoint.json` (env), `/data/quote-engine/checkpoint.json` (C, on `quote_engine_data` named volume), `/tmp/quote-engine/checkpoint.json` (B, ephemeral) | Crash-recovery checkpoint location. **Must** map to a persistent volume in containers. Azure today is ephemeral — known limitation. |
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

Basket registry. In-memory seed today; issuer feeds + corp-action
pipeline still live in the legacy monolith.

| Env var | Required? | Default | Purpose |
|---------|-----------|---------|---------|
| `ASPNETCORE_URLS` | yes (containers) | `http://+:8080` (B) | Bind address. |
| `Kafka__*` | optional today | — | Reserved for future basket-event publishing. |
| `Redis__Configuration` | optional today | — | Reserved for future basket-state mirror. |
| `Timescale__ConnectionString` | optional today | — | Reserved for future basket persistence. |

| Surface | Container DNS:port | External port |
|---------|--------------------|---------------|
| Host (`dotnet run`) | `localhost:5020` | `5020` |
| Containerized (D3) | `hqqq-reference-data:8080` | `5020` (host) |
| Azure Container Apps (D4) | `<referenceDataInternalFqdn>` (internal-only) | none |

Dependencies (today): none beyond startup config binding. The gateway
aggregator scrapes `/healthz/ready`.

---

## `hqqq-ingress` (worker)

Tiingo ingest worker — **stub** today. Real Tiingo ingestion still
lives in the legacy `hqqq-api` monolith. The host runs to expose its
management surface so the gateway aggregator can include it in
`/api/system/health`.

| Env var | Required? | Default | Purpose |
|---------|-----------|---------|---------|
| `Tiingo__ApiKey` | required for real ingestion (deferred) | empty | When live ingestion is wired. |
| `Tiingo__WsUrl` | optional | `wss://api.tiingo.com/iex` | Tiingo IEX WebSocket. |
| `Tiingo__RestBaseUrl` | optional | `https://api.tiingo.com/iex` | Tiingo IEX REST. |
| `Kafka__BootstrapServers` | required when live | — | Will produce `market.raw_ticks.v1` and `market.latest_by_symbol.v1`. |
| `Management__*` | yes (containers) | as for quote-engine | Management host. |

| Surface | Container DNS:port | External port |
|---------|--------------------|---------------|
| Host (`dotnet run`) | management on `localhost:<mgmtPort>` | management only |
| Containerized (D3) | `hqqq-ingress:8081` | `5081` (host) |
| Azure Container Apps (D4) | `<ingressInternalFqdn>` (internal-only) | none |

Dependencies: none on the data plane today (stub).

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
