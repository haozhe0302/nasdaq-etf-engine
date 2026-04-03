namespace Hqqq.Api.Modules.MarketData.Contracts;

/// <summary>
/// The most recent price observation held in memory for a single symbol.
/// </summary>
public sealed record LatestPriceState
{
    public required string Symbol { get; init; }
    public required decimal Price { get; init; }
    public required DateTimeOffset ReceivedAtUtc { get; init; }

    /// <summary>"ws" or "rest".</summary>
    public required string Source { get; init; }

    public bool IsStale { get; init; }
    public decimal? PreviousClose { get; init; }
    public decimal? BidPrice { get; init; }
    public decimal? AskPrice { get; init; }
    public DateTimeOffset? LastTradeTimestampUtc { get; init; }
    public SymbolRole Role { get; init; }
}
