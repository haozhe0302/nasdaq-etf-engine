namespace Hqqq.Api.Modules.Basket.Contracts;

/// <summary>
/// Quality metrics for a merged hybrid basket.
/// </summary>
public sealed record BasketQualityReport
{
    public required decimal OfficialWeightCoveragePct { get; init; }
    public required decimal OfficialSharesCoveragePct { get; init; }
    public required decimal ProxyTailCoveragePct { get; init; }
    public required int FilteredRowCount { get; init; }
    public required int DroppedDirtySymbolCount { get; init; }
    public required int TotalSymbolCount { get; init; }
    public required bool IsDegraded { get; init; }
    public required decimal TotalWeightPct { get; init; }

    /// <summary>"hybrid", "degraded", or "anchor-only".</summary>
    public required string BasketMode { get; init; }

    public required string AnchorSource { get; init; }
    public required string TailSource { get; init; }
    public required int AnchorCount { get; init; }
    public required int TailCount { get; init; }
}
