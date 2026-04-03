namespace Hqqq.Api.Modules.Basket.Contracts;

/// <summary>
/// Quality metrics for a merged hybrid basket.
/// </summary>
public sealed record BasketQualityReport
{
    /// <summary>Percentage of total basket weight covered by anchor (official disclosed) sources.</summary>
    public required decimal OfficialWeightCoveragePct { get; init; }

    /// <summary>Percentage of total basket weight where official shares are available.</summary>
    public required decimal OfficialSharesByWeightPct { get; init; }

    /// <summary>Count of constituents with official shares vs total constituents (for reference).</summary>
    public required int OfficialSharesCount { get; init; }

    /// <summary>Percentage of total basket weight filled by proxy/derived tail sources.</summary>
    public required decimal ProxyTailCoveragePct { get; init; }

    /// <summary>Total number of rows considered from the tail source before filtering.</summary>
    public required int FilteredRowCount { get; init; }

    /// <summary>Rows dropped due to dirty symbols (n/a, cash, futures, malformed).</summary>
    public required int DroppedDirtySymbolCount { get; init; }

    /// <summary>Rows dropped because they were not in the Nasdaq constituent universe.</summary>
    public int UniverseDroppedCount { get; init; }

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
