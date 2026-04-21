# hqqq-reference-data

Phase 2 reference-data service. Owns the **active basket** lifecycle for
HQQQ in both `hybrid` and `standalone` operating modes: fetches or loads a
realistic ~100-name basket, validates and fingerprints it, activates it
in-memory, and publishes the **full constituent payload** on
`refdata.basket.active.v1` for quote-engine / gateway to consume.

## What runs where

Basket ownership **no longer varies by operating mode**. The only
difference between hybrid and standalone is ingress — where ticks come
from. The basket pipeline is identical in both postures.

## HTTP surface

| Method | Path | Behaviour |
|--------|------|-----------|
| `GET`  | `/healthz/live` | liveness |
| `GET`  | `/healthz/ready` | readiness — three-state machine (`Healthy` / `Degraded` / `Unhealthy`). `Degraded` and `Unhealthy` both return **HTTP 503** so K8s-style probes actually react. Surfaces basketId + version + asOfDate + fingerprint + constituent count + **source** (live vs fallback) **and** `lastPublishOkUtc` / `consecutivePublishFailures` / `lastPublishError` / `lastPublishedFingerprint` / `currentFingerprintPublished` / `publishOutageSeconds`. |
| `GET`  | `/api/basket/current` | full active basket (metadata + every constituent) + `publishStatus` sub-object mirroring the readiness state; 503 before first refresh |
| `POST` | `/api/basket/refresh` | real refresh: returns `changed`/`unchanged` + fingerprints + source + count |
| `GET`  | `/metrics` | Prometheus metrics — including `hqqq_refdata_last_publish_ok_timestamp`, `hqqq_refdata_consecutive_publish_failures`, `hqqq_refdata_publish_failures_total`, `hqqq_refdata_publish_outage_seconds` |

## Holdings source pipeline

```
IHoldingsSource ── CompositeHoldingsSource
                    ├── LiveHoldingsSource      (None | File | Http)
                    └── FallbackSeedHoldingsSource  (Resources/basket-seed.json)
```

- **Live first** — when `ReferenceData:LiveHoldings:SourceType` is `File`
  or `Http`, the composite tries the live source, validates the payload,
  and uses it if it passes the validator.
- **Fallback on unavailability/invalid** — any unavailability (disabled,
  missing file, non-2xx HTTP, malformed JSON, failed validation) falls
  through to the committed deterministic seed. The lineage tag on the
  activated basket (`live:file`, `live:http`, `fallback-seed`) makes the
  source decision visible in logs, health, REST, and Kafka.

### Fallback seed

`Resources/basket-seed.json` ships with a realistic ~100-name Nasdaq-100
universe: `symbol`, `name`, `sector`, `sharesHeld`, `referencePrice`,
`targetWeight`. Stable alphabetical ordering and a canonicalized content
fingerprint so restarts on the same image produce the same fingerprint.

## Fingerprinting

`HoldingsFingerprint.Compute` emits a SHA-256 hex over the canonicalized
snapshot: `basketId`, `version`, `asOfDate`, `scaleFactor`, `navPreviousClose`,
`qqqPreviousClose`, and `constituents[]` (sorted by symbol ordinal,
projecting symbol/name/sector/shares/price/weight). Lineage (`Source`) is
intentionally **excluded** — flipping the same basket between live and
seed must not churn the fingerprint.

## Configuration

Everything lives under the `ReferenceData` section (see
`appsettings.json`). Override via env vars using the double-underscore
convention (`ReferenceData__LiveHoldings__SourceType=File`).

| Key | Default | Notes |
|---|---|---|
| `ReferenceData:SeedPath` | _(null)_ | Optional override path for the fallback seed JSON. Unset → embedded resource. |
| `ReferenceData:LiveHoldings:SourceType` | `None` | `None` / `File` / `Http`. |
| `ReferenceData:LiveHoldings:FilePath` | _(null)_ | Used when `SourceType=File`. |
| `ReferenceData:LiveHoldings:HttpUrl` | _(null)_ | Used when `SourceType=Http`. |
| `ReferenceData:LiveHoldings:HttpTimeoutSeconds` | `10` | HTTP per-request timeout. |
| `ReferenceData:LiveHoldings:StaleAfterHours` | `0` | `0` disables the `asOfDate`-based staleness check. |
| `ReferenceData:Refresh:IntervalSeconds` | `600` | Periodic refresh cadence. `0` disables the loop (startup-only). |
| `ReferenceData:Refresh:RepublishIntervalSeconds` | `300` | Slow re-publish so late consumers hydrate. `0` disables. |
| `ReferenceData:Refresh:StartupMaxWaitSeconds` | `30` | Upper bound on the startup refresh attempt. |
| `ReferenceData:Validation:Strict` | `true` | Strict: any validator error → fall back to seed. Permissive: tolerate per-row issues. |
| `ReferenceData:Validation:MinConstituents` | `50` | Soft lower bound. |
| `ReferenceData:Validation:MaxConstituents` | `150` | Soft upper bound (guards duplicated feeds). |
| `ReferenceData:Publish:TopicName` | _(null)_ | Override `refdata.basket.active.v1`. |
| `ReferenceData:PublishHealth:FirstActivationGraceSeconds` | `60` | Grace window after first activation before a never-published basket degrades readiness. |
| `ReferenceData:PublishHealth:DegradedAfterConsecutiveFailures` | `1` | Consecutive publish failures before `/healthz/ready` flips to Degraded (503). |
| `ReferenceData:PublishHealth:UnhealthyAfterConsecutiveFailures` | `5` | Consecutive publish failures before `/healthz/ready` flips to Unhealthy (503). |
| `ReferenceData:PublishHealth:MaxSilenceSeconds` | `900` | Maximum tolerated silence between successful publishes before Unhealthy. |

Runtime logic never assumes **exactly 100 names** — the count is derived
from data and the validator uses configurable soft bounds so 99/100/101
drifts are accepted.

## Published event

`refdata.basket.active.v1` carries the **full** active-basket payload
(not header-only):

- basket identity: `BasketId`, `Version`, `Fingerprint`, `AsOfDate`, `ActivatedAtUtc`
- every constituent: `Symbol`, `SecurityName`, `Sector`, `TargetWeight`,
  `SharesHeld`, `SharesOrigin`
- pricing basis: entries (`Symbol`, `Shares`, `ReferencePrice`,
  `SharesOrigin`, `TargetWeight`), fingerprint, inferred notional,
  counts
- calibration: `ScaleFactor`, `NavPreviousClose`, `QqqPreviousClose`
- lineage: `Source` (live vs fallback), `ConstituentCount`

## Folder structure

```
hqqq-reference-data/
├── Configuration/
│   └── ReferenceDataOptions.cs
├── Endpoints/
│   └── BasketEndpoints.cs
├── Health/
│   └── ActiveBasketHealthCheck.cs
├── Jobs/
│   └── BasketRefreshJob.cs        # startup + periodic refresh + slow republish
├── Models/
│   └── BasketCurrentResponse.cs
├── Publishing/
│   ├── IBasketPublisher.cs
│   ├── KafkaBasketPublisher.cs
│   └── ActiveBasketEventMapper.cs
├── Resources/
│   └── basket-seed.json           # ~100-name deterministic fallback seed
├── Services/
│   ├── ActiveBasketStore.cs
│   ├── BasketRefreshPipeline.cs
│   ├── BasketService.cs
│   ├── IBasketService.cs
│   ├── PublishHealthTracker.cs          # publish attempt/success/failure state
│   ├── PublishHealthStateEvaluator.cs   # shared Healthy/Degraded/Unhealthy logic
│   └── PublishHealthMetrics.cs          # observable Prometheus gauges
├── Sources/
│   ├── IHoldingsSource.cs
│   ├── HoldingsSnapshot.cs
│   ├── HoldingsFetchResult.cs
│   ├── HoldingsFileSchema.cs
│   ├── HoldingsValidator.cs
│   ├── HoldingsFingerprint.cs
│   ├── BasketSeedLoader.cs
│   ├── FallbackSeedHoldingsSource.cs
│   ├── LiveHoldingsSource.cs
│   └── CompositeHoldingsSource.cs
└── Program.cs
```

## Known limitations

- `LiveHoldingsSource` supports `File` and `Http` drops only. Provider-
  specific scrape adapters (Schwab / StockAnalysis / AlphaVantage) still
  live in the legacy monolith and can be ported behind
  `IHoldingsSource` later without touching the refresh pipeline.
- The fallback seed `asOfDate` is pinned at build time; the file is a
  credible stand-in, not a live market snapshot.
