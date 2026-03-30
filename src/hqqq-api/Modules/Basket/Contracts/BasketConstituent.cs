namespace Hqqq.Api.Modules.Basket.Contracts;

/// <summary>
/// A single constituent in the ETF basket composition as of a reference date.
/// </summary>
public sealed record BasketConstituent
{
    /// <summary>Ticker symbol (e.g. "AAPL").</summary>
    public required string Symbol { get; init; }

    /// <summary>Full security name (e.g. "Apple Inc.").</summary>
    public required string SecurityName { get; init; }

    /// <summary>Primary listing exchange (e.g. "NASDAQ").</summary>
    public required string Exchange { get; init; }

    /// <summary>ISO 4217 currency code (e.g. "USD").</summary>
    public required string Currency { get; init; }

    /// <summary>Number of shares held in the basket.</summary>
    public required decimal SharesHeld { get; init; }

    /// <summary>Weight in the basket (0–1). Null when not yet calculated.</summary>
    public decimal? Weight { get; init; }

    /// <summary>Reference date for this composition snapshot.</summary>
    public required DateOnly AsOfDate { get; init; }
}
