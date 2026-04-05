namespace Hqqq.Api.Modules.System.Contracts;

/// <summary>
/// Point-in-time snapshot of runtime observability metrics,
/// served via the /api/system/health endpoint for frontend consumption.
/// Prometheus histograms are the source of truth for scraping;
/// this snapshot provides pre-computed percentiles for the REST API.
/// </summary>
public sealed record RuntimeMetricsSnapshot
{
    public double SnapshotAgeMs { get; init; }
    public double PricedWeightCoverage { get; init; }
    public double StaleSymbolRatio { get; init; }
    public LatencyStats TickToQuoteMs { get; init; } = LatencyStats.Empty;
    public LatencyStats QuoteBroadcastMs { get; init; } = LatencyStats.Empty;
    public double? LastFailoverRecoverySeconds { get; init; }
    public double? LastActivationJumpBps { get; init; }
    public long TotalTicksIngested { get; init; }
    public long TotalQuoteBroadcasts { get; init; }
    public long TotalFallbackActivations { get; init; }
    public long TotalBasketActivations { get; init; }
}

/// <summary>
/// Pre-computed percentile statistics from a rolling window of observations.
/// </summary>
public sealed record LatencyStats
{
    public double P50 { get; init; }
    public double P95 { get; init; }
    public double P99 { get; init; }
    public long SampleCount { get; init; }

    public static readonly LatencyStats Empty = new();
}
