namespace Hqqq.Api.Modules.Pricing.Contracts;

/// <summary>
/// Real-time data freshness metrics for the quote snapshot.
/// </summary>
public sealed record FreshnessInfo
{
    public required int SymbolsTotal { get; init; }
    public required int SymbolsFresh { get; init; }
    public required int SymbolsStale { get; init; }
    public required decimal FreshPct { get; init; }
    public DateTimeOffset? LastTickUtc { get; init; }
    public double? AvgTickIntervalMs { get; init; }
}
