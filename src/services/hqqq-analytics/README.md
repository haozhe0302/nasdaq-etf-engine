# hqqq-analytics

Offline report / replay analytics over persisted Timescale data. **Not on the
hot path** — no live Kafka consumption, no Redis, no HTTP to other services.

## C4 scope — report/replay skeleton

Phase 2C4 turns `hqqq-analytics` from a host skeleton into a useful **one-shot
report runner**:

- reads persisted `quote_snapshots` (optionally a cheap `raw_ticks` aggregate)
  for a requested basket and UTC window,
- computes a deterministic quality / tracking summary,
- logs the summary and optionally emits a JSON artifact,
- stops the host cleanly when the job returns (success or failure).

Full tick-to-snapshot replay, anomaly detection, and backfill pipelines are
**not** implemented here — they live behind the interface seams described
below so they can plug in additively in later phases.

## Inputs

| Table | Required | Purpose |
|-------|----------|---------|
| `quote_snapshots` | yes | Source of every metric the C4 summary computes. |
| `raw_ticks` | no | Optional cheap `count(*)` aggregate when `Analytics:IncludeRawTickAggregates=true`. |

Schema is owned by `hqqq-persistence`. Analytics **does not** run schema
bootstrappers.

## Configuration (`Analytics:` section)

| Key | Default | Description |
|-----|---------|-------------|
| `Mode` | `report` | Only `report` is implemented in C4. Unknown modes log an error and exit with code 2. |
| `BasketId` | `HQQQ` | Basket filter applied to `quote_snapshots.basket_id`. |
| `StartUtc` | _required_ | Inclusive lower bound of the report window. |
| `EndUtc` | _required_ | Inclusive upper bound of the report window. Must be strictly after `StartUtc`. |
| `EmitJsonPath` | _null_ | Optional filesystem path for a pretty-printed JSON copy of the summary. Parent directories are created on demand. |
| `MaxRows` | `1000000` | Hard cap on rows loaded per run. Exceeding it fails fast rather than silently truncating. |
| `IncludeRawTickAggregates` | `false` | When true, attach a cheap `raw_ticks` count to the summary. |
| `StaleQualityStates` | `["stale","degraded"]` | Case-insensitive `quote_quality` values counted in the stale ratio. |
| `TopGapCount` | `5` | Top-N largest detected inter-snapshot gaps reported. |

`Timescale:ConnectionString` is bound from shared infrastructure options as
usual (flat-key env fallback supported via `LegacyConfigShim`).

## Summary shape

The calculator is pure (`SnapshotQualityCalculator.Compute`) and produces a
`ReportSummary` with the following fields:

| Group | Fields |
|-------|--------|
| Identity | `basketId`, `requestedStartUtc`, `requestedEndUtc` |
| Window coverage | `actualFirstUtc?`, `actualLastUtc?`, `pointCount`, `hasData` |
| Density | `medianIntervalMs?`, `p95IntervalMs?`, `pointsPerMinute?` |
| Quality | `staleRatio`, `maxComponentAgeMsP50`, `maxComponentAgeMsP95`, `maxComponentAgeMsMax`, `quoteQualityCounts` |
| Basis / tracking | `rmseBps`, `maxAbsBasisBps`, `avgAbsBasisBps`, `correlation?` |
| Coverage | `tradingDaysCovered`, `daysCovered` |
| Gaps | `largestGaps[]` (top-N by duration) |
| Raw ticks | `rawTickCount?` (only when opted in) |

An empty window produces `hasData=false` with zeroed numeric fields and no
`NaN`s — the host logs a single `WARN` and exits cleanly.

## Running a report

```powershell
# Minimal report, summary logged only
$env:Timescale__ConnectionString = "Host=localhost;Port=5432;Database=hqqq;Username=admin;Password=changeme"
$env:Analytics__StartUtc = "2026-04-17T00:00:00Z"
$env:Analytics__EndUtc   = "2026-04-18T00:00:00Z"
dotnet run --project src/services/hqqq-analytics/hqqq-analytics.csproj

# Report with JSON artifact
$env:Analytics__EmitJsonPath = "artifacts/hqqq-2026-04-17.json"
dotnet run --project src/services/hqqq-analytics/hqqq-analytics.csproj

# Include cheap raw-tick aggregate
$env:Analytics__IncludeRawTickAggregates = "true"
dotnet run --project src/services/hqqq-analytics/hqqq-analytics.csproj
```

Exit codes:

| Code | Meaning |
|------|---------|
| `0` | Report ran successfully (including empty-window). |
| `1` | Report job threw (reader failure, artifact write failure, etc.). |
| `2` | `Analytics:Mode` not implemented in C4. |

## Future extension seams

Interfaces exist in `Services/` so later phases can plug in implementations
without reshaping DI, options, or the dispatcher:

- `IReportJob` — common shape for any `Mode` the dispatcher selects from.
- `IReplayJob` — Phase 2C5+: tick-to-snapshot replay.
- `IAnomalyDetector` — Phase 2D+: streaming detectors over snapshots or ticks.
- `IBackfillJob` — Phase 2D+: re-ingest / re-materialize missing data.

These are **deliberately unregistered** in DI today; the `ReportJobDispatcher`
fails cleanly on unknown modes.

## Explicitly deferred beyond C4

- Full raw-tick → snapshot replay engine
- Anomaly detection pipeline
- Backfill workflow
- Multi-basket or cron-style scheduling
- REST or UI surface for reports
- Persistence schema changes (none in this phase)
- Live Kafka consumption in analytics
- Comparison against unreconstructed external anchor services
