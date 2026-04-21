using System.Diagnostics.Metrics;
using Hqqq.Observability.Metrics;

namespace Hqqq.ReferenceData.Services;

/// <summary>
/// Wires observable gauges that project the current
/// <see cref="PublishHealthTracker"/> state into the Prometheus scrape:
/// <list type="bullet">
///   <item><c>hqqq_refdata_last_publish_ok_timestamp</c> — unix seconds, 0 if never.</item>
///   <item><c>hqqq_refdata_consecutive_publish_failures</c> — current streak length.</item>
///   <item><c>hqqq_refdata_publish_outage_seconds</c> — elapsed seconds since the last successful publish when in a failure state, else 0.</item>
/// </list>
/// The failure counter (<c>hqqq_refdata_publish_failures_total</c>) lives on
/// <see cref="HqqqMetrics"/> and is incremented from the refresh pipeline.
/// Registered as a singleton so the observable callbacks stay alive for
/// the lifetime of the host.
/// </summary>
public sealed class PublishHealthMetrics : IDisposable
{
    private readonly PublishHealthTracker _tracker;
    private readonly TimeProvider _clock;

    public PublishHealthMetrics(PublishHealthTracker tracker, TimeProvider? clock = null)
    {
        _tracker = tracker;
        _clock = clock ?? TimeProvider.System;

        HqqqMetrics.Meter.CreateObservableGauge(
            MetricNames.RefdataLastPublishOkTimestamp,
            ObserveLastPublishOkTimestamp,
            unit: "s");

        HqqqMetrics.Meter.CreateObservableGauge(
            MetricNames.RefdataConsecutivePublishFailures,
            ObserveConsecutivePublishFailures,
            unit: "failures");

        HqqqMetrics.Meter.CreateObservableGauge(
            MetricNames.RefdataPublishOutageSeconds,
            ObserveOutageSeconds,
            unit: "s");
    }

    private long ObserveLastPublishOkTimestamp()
    {
        var snap = _tracker.Snapshot;
        return snap.LastPublishOkUtc is null ? 0 : snap.LastPublishOkUtc.Value.ToUnixTimeSeconds();
    }

    private int ObserveConsecutivePublishFailures() => _tracker.Snapshot.ConsecutivePublishFailures;

    private double ObserveOutageSeconds()
    {
        var snap = _tracker.Snapshot;
        if (snap.ConsecutivePublishFailures == 0) return 0;
        if (snap.LastPublishOkUtc is null) return 0; // never-published captured by gauge above
        var outage = (_clock.GetUtcNow() - snap.LastPublishOkUtc.Value).TotalSeconds;
        return Math.Max(0, outage);
    }

    public void Dispose()
    {
        // Gauge callbacks stop being invoked when the Meter is disposed.
        // The static meter lives for the life of the process, so nothing
        // to dispose here — but IDisposable keeps DI ergonomics clean.
    }
}
