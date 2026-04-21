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
}
