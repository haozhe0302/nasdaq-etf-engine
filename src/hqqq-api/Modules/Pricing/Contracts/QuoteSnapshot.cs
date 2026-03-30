namespace Hqqq.Api.Modules.Pricing.Contracts;

/// <summary>
/// A point-in-time snapshot of the ETF's indicative pricing.
/// </summary>
public sealed record QuoteSnapshot
{
    /// <summary>ETF ticker symbol (e.g. "HQQQ").</summary>
    public required string Symbol { get; init; }

    /// <summary>Calculated indicative net asset value per share.</summary>
    public required decimal IndicativeNav { get; init; }

    /// <summary>Current market price. Null before first market trade.</summary>
    public decimal? MarketPrice { get; init; }

    /// <summary>Premium/discount vs. iNAV as a percentage. Null when market price is unavailable.</summary>
    public decimal? PremiumDiscountPct { get; init; }

    /// <summary>Total market value of the underlying basket.</summary>
    public required decimal BasketMarketValue { get; init; }

    /// <summary>Estimated cash component per creation unit.</summary>
    public decimal? CashComponent { get; init; }

    /// <summary>Total shares outstanding of the ETF.</summary>
    public decimal? SharesOutstanding { get; init; }

    /// <summary>UTC timestamp of this snapshot.</summary>
    public required DateTimeOffset AsOfUtc { get; init; }
}
