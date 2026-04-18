namespace Hqqq.Analytics.Reports;

/// <summary>
/// Deterministic, immutable quality summary emitted by a single run of the
/// C4 report job. Every field is either populated from the input snapshots
/// or set to a stable zero/empty default so consumers (logs, JSON artifact,
/// future downstreams) never see <c>NaN</c> or undefined state.
/// </summary>
/// <remarks>
/// Numeric bps metrics are expressed as basis points of <c>MarketProxyPrice</c>
/// relative to <c>Nav</c>: <c>(market - nav) / nav * 10_000</c>.
/// </remarks>
public sealed record ReportSummary
{
    public required string BasketId { get; init; }
    public required DateTimeOffset RequestedStartUtc { get; init; }
    public required DateTimeOffset RequestedEndUtc { get; init; }

    public DateTimeOffset? ActualFirstUtc { get; init; }
    public DateTimeOffset? ActualLastUtc { get; init; }
    public required long PointCount { get; init; }

    public double? MedianIntervalMs { get; init; }
    public double? P95IntervalMs { get; init; }
    public double? PointsPerMinute { get; init; }

    public required double StaleRatio { get; init; }

    public required double MaxComponentAgeMsP50 { get; init; }
    public required double MaxComponentAgeMsP95 { get; init; }
    public required double MaxComponentAgeMsMax { get; init; }

    public required IReadOnlyDictionary<string, long> QuoteQualityCounts { get; init; }

    public required double RmseBps { get; init; }
    public required double MaxAbsBasisBps { get; init; }
    public required double AvgAbsBasisBps { get; init; }
    public double? Correlation { get; init; }

    public required int TradingDaysCovered { get; init; }
    public required int DaysCovered { get; init; }

    public required IReadOnlyList<TimeGap> LargestGaps { get; init; }

    public long? RawTickCount { get; init; }

    public required bool HasData { get; init; }
}

/// <summary>
/// Represents a detected gap between two consecutive snapshots within the
/// requested window.
/// </summary>
public sealed record TimeGap(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    double DurationMs);
