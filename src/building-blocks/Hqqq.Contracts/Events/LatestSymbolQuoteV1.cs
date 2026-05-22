namespace Hqqq.Contracts.Events;

/// <summary>
/// Latest quote per symbol on the compacted <c>market.latest_by_symbol.v1</c> topic.
/// Used by quote-engine for fast bootstrap on failover.
/// Key: <see cref="Symbol"/>.
/// </summary>
public sealed record LatestSymbolQuoteV1
{
    public required string Symbol { get; init; }
    public required decimal Last { get; init; }
    public decimal? Bid { get; init; }
    public decimal? Ask { get; init; }
    public required string Currency { get; init; }
    public required string Provider { get; init; }
    public required DateTimeOffset ProviderTimestamp { get; init; }
    public required DateTimeOffset IngressTimestamp { get; init; }
    public required bool IsStale { get; init; }

    /// <summary>
    /// Previous regular-session close. Carried so a consumer cold-starting
    /// off the compacted topic can compute today's %-change for symbols
    /// without waiting for the next REST snapshot. Optional and additive.
    /// </summary>
    public decimal? PreviousClose { get; init; }
}
