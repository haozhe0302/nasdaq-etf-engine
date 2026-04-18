using Hqqq.Analytics.Options;

namespace Hqqq.Analytics.Services;

/// <summary>
/// Future seam for a backfill job. Reserved for Phase 2D+: re-ingest or
/// re-materialize rows for a requested window when upstream replay discovers
/// missing or corrupt data. Intentionally not implemented in C4 — present
/// only so the dispatcher and DI wiring do not have to be reshaped later.
/// </summary>
public interface IBackfillJob
{
    Task RunAsync(AnalyticsOptions options, CancellationToken ct);
}
