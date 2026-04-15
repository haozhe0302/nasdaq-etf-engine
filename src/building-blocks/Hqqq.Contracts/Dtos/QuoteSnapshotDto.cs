namespace Hqqq.Contracts.Dtos;

/// <summary>
/// REST / SignalR serving shape for the latest iNAV snapshot.
/// </summary>
public sealed record QuoteSnapshotDto
{
    public required decimal Nav { get; init; }
    public required decimal NavChangePct { get; init; }
    public required decimal MarketPrice { get; init; }
    public required decimal PremiumDiscountPct { get; init; }
    public required decimal Qqq { get; init; }
    public required decimal QqqChangePct { get; init; }
    public required decimal BasketValueB { get; init; }
    public required DateTimeOffset AsOf { get; init; }
    public required string QuoteState { get; init; }
    public required bool IsLive { get; init; }
    public required bool IsFrozen { get; init; }
    public string? PauseReason { get; init; }
}

/// <summary>
/// A single point in a time-series (e.g. NAV vs market chart).
/// </summary>
public sealed record SeriesPointDto
{
    public required DateTimeOffset Time { get; init; }
    public required decimal Nav { get; init; }
    public required decimal Market { get; init; }
}

/// <summary>
/// Top mover / laggard in the basket.
/// </summary>
public sealed record MoverDto
{
    public required string Symbol { get; init; }
    public required string Name { get; init; }
    public required decimal ChangePct { get; init; }
    public required decimal Impact { get; init; }
    public required string Direction { get; init; }
}

/// <summary>
/// Tick freshness summary across all constituents.
/// </summary>
public sealed record FreshnessDto
{
    public required int SymbolsTotal { get; init; }
    public required int SymbolsFresh { get; init; }
    public required int SymbolsStale { get; init; }
    public required decimal FreshPct { get; init; }
    public DateTimeOffset? LastTickUtc { get; init; }
    public double? AvgTickIntervalMs { get; init; }
}
