namespace Hqqq.Api.Modules.MarketData.Contracts;

/// <summary>
/// A single price observation for a constituent security.
/// </summary>
public sealed record PriceTick
{
    /// <summary>Ticker symbol (e.g. "AAPL").</summary>
    public required string Symbol { get; init; }

    /// <summary>Observed price in the security's trading currency.</summary>
    public required decimal Price { get; init; }

    /// <summary>ISO 4217 currency code (e.g. "USD").</summary>
    public required string Currency { get; init; }

    /// <summary>"ws" or "rest".</summary>
    public required string Source { get; init; }

    /// <summary>UTC timestamp of the market event.</summary>
    public required DateTimeOffset EventTimeUtc { get; init; }

    public decimal? BidPrice { get; init; }
    public decimal? AskPrice { get; init; }
    public decimal? PreviousClose { get; init; }
    public DateTimeOffset? LastTradeTimestampUtc { get; init; }
}
