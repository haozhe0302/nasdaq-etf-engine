namespace Hqqq.Contracts.Dtos;

/// <summary>
/// Composite serving shape for the latest constituents snapshot. Materialized
/// by the quote-engine to Redis on every compute cycle and served via REST by
/// the gateway. Field shape is intentionally aligned with the current
/// frontend <c>BConstituentSnapshot</c> adapter so gateway readers can
/// deserialize the cached payload without an intermediate remap.
/// </summary>
public sealed record ConstituentsSnapshotDto
{
    public required IReadOnlyList<ConstituentHoldingDto> Holdings { get; init; }
    public required ConcentrationDto Concentration { get; init; }
    public required BasketQualityDto Quality { get; init; }
    public required BasketSourceDto Source { get; init; }
    public required DateTimeOffset AsOf { get; init; }
}

/// <summary>
/// A single row in the constituents table: identity, sector, weight, shares,
/// and the latest price snapshot for the symbol.
/// </summary>
public sealed record ConstituentHoldingDto
{
    public required string Symbol { get; init; }
    public required string Name { get; init; }
    public required string Sector { get; init; }
    public required decimal Weight { get; init; }
    public required decimal Shares { get; init; }
    public decimal? Price { get; init; }
    public decimal? ChangePct { get; init; }
    public decimal? MarketValue { get; init; }
    public required string SharesOrigin { get; init; }
    public required bool IsStale { get; init; }
}

/// <summary>
/// Concentration metrics derived from the basket weight distribution.
/// </summary>
public sealed record ConcentrationDto
{
    public required decimal Top5Pct { get; init; }
    public required decimal Top10Pct { get; init; }
    public required decimal Top20Pct { get; init; }
    public required int SectorCount { get; init; }
    public required decimal HerfindahlIndex { get; init; }
}

/// <summary>
/// Basket data-quality summary: counts of tracked / priced / stale symbols
/// plus shares-provenance breakdown.
/// </summary>
public sealed record BasketQualityDto
{
    public required int TotalSymbols { get; init; }
    public required int OfficialSharesCount { get; init; }
    public required int DerivedSharesCount { get; init; }
    public required int PricedCount { get; init; }
    public required int StaleCount { get; init; }
    public required decimal PriceCoveragePct { get; init; }
    public required string BasketMode { get; init; }
}

/// <summary>
/// Provenance metadata about the basket snapshot sources and its identity.
/// B4 fills <see cref="Fingerprint"/> and <see cref="AsOfDate"/> from the
/// active basket; richer anchor/tail provenance lands with the gateway cut-over.
/// </summary>
public sealed record BasketSourceDto
{
    public required string AnchorSource { get; init; }
    public required string TailSource { get; init; }
    public required string BasketMode { get; init; }
    public required bool IsDegraded { get; init; }
    public required string AsOfDate { get; init; }
    public required string Fingerprint { get; init; }
}
