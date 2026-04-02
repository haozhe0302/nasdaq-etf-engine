namespace Hqqq.Api.Modules.MarketData.Contracts;

/// <summary>
/// The most recent price observation held in memory for a single symbol.
/// </summary>
public sealed record LatestPriceState
{
    public required string Symbol { get; init; }
    public required decimal Price { get; init; }
    public required DateTimeOffset ReceivedAtUtc { get; init; }
    public required string Source { get; init; }
    public bool IsStale { get; init; }
}
