# hqqq-reference-data

Phase 2 reference-data service. Owns the **active basket** lifecycle for
HQQQ: fetches or loads a realistic ~100-name basket, validates and
fingerprints it, applies Phase-2-native **corporate-action adjustments**
(splits, renames, constituent transitions), and publishes the **full
constituent payload** on `refdata.basket.active.v1` for quote-engine /
gateway / ingress to consume.

## Runtime posture

Basket ownership is unconditional in Phase 2 — there is no
hybrid/standalone runtime split for reference-data anymore. The legacy
`hqqq-api` monolith is not part of the Phase 2 runtime path; it remains
in the repo as reference code only.

## HTTP surface

| Method | Path | Behaviour |
|--------|------|-----------|
| `GET`  | `/healthz/live` | liveness |
| `GET`  | `/healthz/ready` | readiness — three-state machine (`Healthy` / `Degraded` / `Unhealthy`). `Degraded` and `Unhealthy` both return **HTTP 503** so K8s-style probes actually react. Surfaces basketId + version + asOfDate + fingerprint + constituent count + **source** (live vs fallback) **and** `lastPublishOkUtc` / `consecutivePublishFailures` / `lastPublishError` / `lastPublishedFingerprint` / `currentFingerprintPublished` / `publishOutageSeconds`. |
| `GET`  | `/api/basket/current` | full active basket (metadata + every constituent) + `publishStatus` sub-object mirroring the readiness state + **`adjustmentSummary`** (splits applied, renames applied, added/removed symbols, source lineage) + `previousFingerprint` / `previousBasketId` for transition continuity; 503 before first refresh |
| `POST` | `/api/basket/refresh` | real refresh: returns `changed`/`unchanged` + fingerprints + source + count |
| `GET`  | `/metrics` | Prometheus metrics — including `hqqq_refdata_last_publish_ok_timestamp`, `hqqq_refdata_consecutive_publish_failures`, `hqqq_refdata_publish_failures_total`, `hqqq_refdata_publish_outage_seconds`, `hqqq_refdata_splits_applied_total`, `hqqq_refdata_renames_applied_total`, `hqqq_refdata_basket_transitions_total`, `hqqq_refdata_corp_action_fetch_errors_total` |

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
| `ReferenceData:CorporateActions:LookbackDays` | `365` | Upper bound on the corp-action window when `snapshot.AsOfDate` is unexpectedly ancient. |
| `ReferenceData:CorporateActions:File:Path` | _(null)_ | Optional override for the corp-action JSON file. Unset → embedded `Resources/corporate-actions-seed.json`. |
| `ReferenceData:CorporateActions:Tiingo:Enabled` | `false` | When `true`, overlay Tiingo EOD splits on top of the file feed. |
| `ReferenceData:CorporateActions:Tiingo:ApiKey` | _(null)_ | Required when `Tiingo:Enabled=true`. |
| `ReferenceData:CorporateActions:Tiingo:BaseUrl` | `https://api.tiingo.com/tiingo/daily` | Tiingo EOD base URL. |
| `ReferenceData:CorporateActions:Tiingo:TimeoutSeconds` | `10` | Tiingo per-request timeout. |
| `ReferenceData:CorporateActions:Tiingo:MaxConcurrency` | `5` | Parallel per-symbol Tiingo requests. |
| `ReferenceData:CorporateActions:Tiingo:CacheTtlMinutes` | `60` | Per-symbol split-cache TTL. |

Runtime logic never assumes **exactly 100 names** — the count is derived
from data and the validator uses configurable soft bounds so 99/100/101
drifts are accepted.

## Corporate-action adjustment (Phase-2-native)

Before fingerprinting + publishing, every refresh runs the snapshot
through the corporate-action layer:

```
IHoldingsSource → HoldingsValidator
               → CorporateActionAdjustmentService
                    (splits + renames, via CompositeCorporateActionProvider:
                     File first + optional Tiingo overlay)
               → BasketTransitionPlanner
                    (add/remove diff + ScaleFactorCalibrator continuity)
               → HoldingsFingerprint → Publish
```

**Supported scope (honest and explicit):**

- Forward splits (factor > 1) and reverse splits (factor < 1) — applied
  to `SharesHeld` for events with `EffectiveDate ∈ (snapshot.AsOfDate, runtimeDate]`.
- Ticker renames / symbol remaps — chained hops resolved to the
  terminal symbol by `SymbolRemapResolver`.
- Constituent add / remove detection across basket transitions.
- Scale-factor continuity via `Hqqq.Domain.Services.ScaleFactorCalibrator`
  when the raw-basket value changes.

**Explicitly not supported:**

- Dividends, special dividends, rights offerings.
- Spin-offs, mergers, acquisitions at the constituent level (Phase 2
  trusts the holdings source for the new constituent).
- Cross-exchange moves, ISIN/CUSIP-level remaps.
- Retroactive re-pricing of already-stored ticks in Timescale.

The `CompositeCorporateActionProvider` reads a deterministic JSON file
first (`Resources/corporate-actions-seed.json` embedded, override path
via `ReferenceData:CorporateActions:File:Path`). When
`ReferenceData:CorporateActions:Tiingo:Enabled=true`, Tiingo EOD splits
are overlaid on top; if Tiingo errors, the composite falls back to
file-only with lineage `file+tiingo-degraded` and surfaces the error on
the `/api/basket/current` adjustment summary. No monolith runtime
dependency.

## Published event

`refdata.basket.active.v1` carries the **full** active-basket payload
(not header-only):

- basket identity: `BasketId`, `Version`, `Fingerprint`, `AsOfDate`, `ActivatedAtUtc`
- every constituent: `Symbol`, `SecurityName`, `Sector`, `TargetWeight`,
  `SharesHeld`, `SharesOrigin`
- pricing basis: entries (`Symbol`, `Shares`, `ReferencePrice`,
  `SharesOrigin`, `TargetWeight`), fingerprint, inferred notional, counts
- calibration: `ScaleFactor`, `NavPreviousClose`, `QqqPreviousClose`
- lineage: `Source` (live vs fallback, suffixed with `+corp-adjusted`
  when the corp-action layer made a change), `ConstituentCount`
- **additive** — transition continuity + adjustment metadata:
  `PreviousBasketId`, `PreviousFingerprint`, `AdjustmentSummary`
  (`SplitsApplied`, `RenamesApplied`, `AddedSymbols`, `RemovedSymbols`,
  `AdjustmentAsOfDate`, `AdjustmentAppliedAtUtc`, `ProviderSource`,
  `ScaleFactorRecalibrated`). Historical messages that pre-date this
  pass deserialize with these fields unset.

## Folder structure

```
hqqq-reference-data/
├── Configuration/
│   └── ReferenceDataOptions.cs
├── CorporateActions/
│   ├── Contracts/
│   │   ├── SplitEvent.cs
│   │   ├── SymbolRenameEvent.cs
│   │   ├── CorporateActionFeed.cs
│   │   ├── AdjustmentReport.cs       # SplitAdjustments, RenameAdjustments, Added/Removed, scale-factor metadata
│   │   └── ICorporateActionProvider.cs
│   ├── Providers/
│   │   ├── CorporateActionFileSchema.cs
│   │   ├── FileCorporateActionProvider.cs
│   │   ├── TiingoCorporateActionProvider.cs   # opt-in via CorporateActions:Tiingo:Enabled
│   │   └── CompositeCorporateActionProvider.cs
│   └── Services/
│       ├── SymbolRemapResolver.cs             # chained rename → terminal symbol
│       ├── CorporateActionAdjustmentService.cs
│       └── BasketTransitionPlanner.cs         # add/remove diff + ScaleFactorCalibrator
├── Endpoints/
│   └── BasketEndpoints.cs
├── Health/
│   ├── ActiveBasketHealthCheck.cs
│   └── CorporateActionHealthCheck.cs
├── Jobs/
│   └── BasketRefreshJob.cs        # startup + periodic refresh + slow republish
├── Models/
│   └── BasketCurrentResponse.cs
├── Publishing/
│   ├── IBasketPublisher.cs
│   ├── KafkaBasketPublisher.cs
│   └── ActiveBasketEventMapper.cs
├── Resources/
│   ├── basket-seed.json                       # ~100-name deterministic fallback seed
│   └── corporate-actions-seed.json            # default corp-action feed (empty)
├── Services/
│   ├── ActiveBasketStore.cs                   # Current + Previous + LatestAdjustmentReport
│   ├── BasketRefreshPipeline.cs
│   ├── BasketService.cs
│   ├── IBasketService.cs
│   ├── PublishHealthTracker.cs
│   ├── PublishHealthStateEvaluator.cs
│   └── PublishHealthMetrics.cs
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
  specific scrape adapters (Schwab / StockAnalysis / AlphaVantage) are
  out of scope for Phase 2 runtime; the legacy monolith retains them as
  reference only.
- The fallback seed `asOfDate` is pinned at build time; the file is a
  credible stand-in, not a live market snapshot.
- Corp-action scope is narrow by design (see *Corporate-action adjustment*
  above). Dividends, spin-offs, mergers, and cross-exchange moves are
  intentionally not supported.
- Splits adjust `SharesHeld` but leave `ReferencePrice` to the next
  holdings refresh; this is a known approximation.
