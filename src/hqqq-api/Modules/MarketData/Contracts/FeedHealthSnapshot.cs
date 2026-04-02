namespace Hqqq.Api.Modules.MarketData.Contracts;

/// <summary>
/// Aggregate health status of the market-data ingestion pipeline.
/// </summary>
public sealed record FeedHealthSnapshot
{
    public required bool WebSocketConnected { get; init; }
    public required int SymbolsTracked { get; init; }
    public required int StaleSymbolCount { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
}
