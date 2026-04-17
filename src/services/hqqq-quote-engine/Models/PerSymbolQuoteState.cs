namespace Hqqq.QuoteEngine.Models;

/// <summary>
/// The most recent price observation held in memory for a single symbol.
/// Mirrors the legacy <c>Hqqq.Api.Modules.MarketData.Contracts.LatestPriceState</c>
/// shape but without coupling to legacy enums.
/// </summary>
public sealed record PerSymbolQuoteState
{
    public required string Symbol { get; init; }
    public required decimal Price { get; init; }
    public required DateTimeOffset ReceivedAtUtc { get; init; }
    public required string Provider { get; init; }
    public required long Sequence { get; init; }
    public decimal? PreviousClose { get; init; }
    public decimal? Bid { get; init; }
    public decimal? Ask { get; init; }
}
