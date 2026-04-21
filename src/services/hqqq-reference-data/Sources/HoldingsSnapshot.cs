namespace Hqqq.ReferenceData.Sources;

/// <summary>
/// Normalized, validated in-memory representation of a basket holdings
/// snapshot. The refresh pipeline works against this type exclusively;
/// file/http wire shapes are mapped into it at the edge so the core never
/// sees provider-specific formats.
/// </summary>
public sealed record HoldingsSnapshot
{
    public required string BasketId { get; init; }
    public required string Version { get; init; }
    public required DateOnly AsOfDate { get; init; }
    public required decimal ScaleFactor { get; init; }
    public decimal? NavPreviousClose { get; init; }
    public decimal? QqqPreviousClose { get; init; }
    public required IReadOnlyList<HoldingsConstituent> Constituents { get; init; }

    /// <summary>Lineage tag (e.g. <c>"live:file"</c>, <c>"live:http"</c>, <c>"fallback-seed"</c>).</summary>
    public required string Source { get; init; }
}

public sealed record HoldingsConstituent
{
    public required string Symbol { get; init; }
    public required string Name { get; init; }
    public required string Sector { get; init; }
    public required decimal SharesHeld { get; init; }
    public required decimal ReferencePrice { get; init; }
    public decimal? TargetWeight { get; init; }

    /// <summary>
    /// Per-row lineage tag for the <see cref="TargetWeight"/> source
    /// (e.g. <c>"stockanalysis"</c>, <c>"schwab"</c>, <c>"alphavantage"</c>,
    /// <c>"nasdaq-proxy"</c>, <c>"file"</c>, <c>"seed"</c>). Null on
    /// historical in-memory rows that pre-date the Phase 1 port; wire
    /// payloads / published events are filled with an explicit value.
    /// </summary>
    public string? WeightSource { get; init; }

    /// <summary>
    /// Per-row lineage tag for <see cref="SharesHeld"/>. <c>"stockanalysis"</c>
    /// / <c>"schwab"</c> for anchor rows that carry authoritative shares;
    /// <c>"unavailable"</c> for tail rows (weight-only, no disclosed
    /// shares); <c>"split-adjusted"</c> suffix after the corporate-action
    /// adjustment layer multiplies by a cumulative split factor.
    /// </summary>
    public string? SharesSource { get; init; }

    /// <summary>Per-row lineage tag for <see cref="Name"/>.</summary>
    public string? NameSource { get; init; }

    /// <summary>Per-row lineage tag for <see cref="Sector"/> (<c>"unknown"</c> when not disclosed).</summary>
    public string? SectorSource { get; init; }
}
