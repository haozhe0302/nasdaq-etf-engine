namespace Hqqq.Api.Modules.Pricing.Contracts;

/// <summary>
/// Frontend-ready constituents view served from GET /api/constituents.
/// Based on the active basket and current pricing basis only.
/// </summary>
public sealed record ConstituentSnapshot
{
    public required IReadOnlyList<ConstituentRow> Holdings { get; init; }
    public required ConcentrationMetrics Concentration { get; init; }
    public required DataQualityMetrics Quality { get; init; }
    public required BasketSourceMetadata Source { get; init; }
    public required DateTimeOffset AsOf { get; init; }
}

public sealed record ConstituentRow
{
    public required string Symbol { get; init; }
    public required string Name { get; init; }
    public required string Sector { get; init; }
    public required decimal Weight { get; init; }
    public required int Shares { get; init; }
    public decimal? Price { get; init; }
    public decimal? ChangePct { get; init; }
    public decimal? MarketValue { get; init; }
    public required string SharesOrigin { get; init; }
    public bool IsStale { get; init; }
}

public sealed record ConcentrationMetrics
{
    public required decimal Top5Pct { get; init; }
    public required decimal Top10Pct { get; init; }
    public required decimal Top20Pct { get; init; }
    public required int SectorCount { get; init; }
    public required decimal HerfindahlIndex { get; init; }
}

public sealed record DataQualityMetrics
{
    public required int TotalSymbols { get; init; }
    public required int OfficialSharesCount { get; init; }
    public required int DerivedSharesCount { get; init; }
    public required int PricedCount { get; init; }
    public required int StaleCount { get; init; }
    public required decimal PriceCoveragePct { get; init; }
    public required string BasketMode { get; init; }
}

public sealed record BasketSourceMetadata
{
    public required string AnchorSource { get; init; }
    public required string TailSource { get; init; }
    public required string BasketMode { get; init; }
    public required bool IsDegraded { get; init; }
    public required DateOnly AsOfDate { get; init; }
    public required string Fingerprint { get; init; }
}
