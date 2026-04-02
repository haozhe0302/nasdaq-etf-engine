namespace Hqqq.Api.Modules.Basket.Contracts;

/// <summary>
/// A single constituent in the ETF basket composition as of a reference date.
/// Provenance fields track which source provided each important value.
/// </summary>
public sealed record BasketConstituent
{
    public required string Symbol { get; init; }
    public required string SecurityName { get; init; }
    public required string Exchange { get; init; }
    public required string Currency { get; init; }
    public required decimal SharesHeld { get; init; }
    public decimal? Weight { get; init; }
    public string Sector { get; init; } = "Unknown";
    public required DateOnly AsOfDate { get; init; }

    // ── Field provenance ──
    public string WeightSource { get; init; } = "unknown";
    public string SharesSource { get; init; } = "unknown";
    public string NameSource { get; init; } = "unknown";
    public string SectorSource { get; init; } = "unknown";
}
