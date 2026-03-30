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

    /// <summary>Data source identifier (e.g. "finnhub", "polygon").</summary>
    public required string Source { get; init; }

    /// <summary>UTC timestamp of the market event.</summary>
    public required DateTimeOffset EventTimeUtc { get; init; }
}
