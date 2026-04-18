using Hqqq.Analytics.Options;

namespace Hqqq.Analytics.Services;

/// <summary>
/// Future seam for a streaming anomaly detector over persisted snapshots or
/// ticks. Reserved for Phase 2D+: plug in detectors (e.g. basis-spike,
/// stale-run, missing-bar) behind this interface so the C4 report job stays
/// a pure summary. Intentionally not implemented in C4.
/// </summary>
public interface IAnomalyDetector
{
    IAsyncEnumerable<AnomalyFinding> DetectAsync(AnalyticsOptions options, CancellationToken ct);
}

/// <summary>
/// Shape of a single anomaly finding. Kept minimal so detectors can extend
/// it later without breaking the seam.
/// </summary>
public sealed record AnomalyFinding(
    string DetectorId,
    string BasketId,
    DateTimeOffset ObservedUtc,
    string Severity,
    string Summary);
