namespace Hqqq.Api.Modules.History.Contracts;

/// <summary>
/// API response for GET /api/history — shaped for frontend consumption.
/// </summary>
public sealed record HistoryResponse
{
    public required string Range { get; init; }
    public required string StartDate { get; init; }
    public required string EndDate { get; init; }
    public required int PointCount { get; init; }
    public required int TotalPoints { get; init; }
    public required bool IsPartial { get; init; }
    public required IReadOnlyList<HistorySeriesPoint> Series { get; init; }
    public required HistoryTrackingStats TrackingError { get; init; }
    public required IReadOnlyList<HistoryDistBucket> Distribution { get; init; }
    public required HistoryDiagnostics Diagnostics { get; init; }
}

public sealed record HistorySeriesPoint
{
    public required DateTimeOffset Time { get; init; }
    public required decimal Nav { get; init; }
    public required decimal MarketPrice { get; init; }
}

public sealed record HistoryTrackingStats
{
    public double RmseBps { get; init; }
    public double MaxAbsBasisBps { get; init; }
    public double AvgAbsBasisBps { get; init; }
    public double MaxDeviationPct { get; init; }
    public double Correlation { get; init; }
}

public sealed record HistoryDistBucket
{
    public required string Label { get; init; }
    public required int Count { get; init; }
}

public sealed record HistoryDiagnostics
{
    public required int Snapshots { get; init; }
    public required int Gaps { get; init; }
    public required double CompletenessPct { get; init; }
    public required int DaysLoaded { get; init; }
}
