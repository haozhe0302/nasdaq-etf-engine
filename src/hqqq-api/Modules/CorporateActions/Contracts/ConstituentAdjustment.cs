namespace Hqqq.Api.Modules.CorporateActions.Contracts;

/// <summary>
/// Per-constituent record of a corporate-action adjustment that was applied.
/// </summary>
public sealed record ConstituentAdjustment
{
    public required string Symbol { get; init; }
    public required decimal OriginalShares { get; init; }
    public required decimal AdjustedShares { get; init; }

    /// <summary>Product of all applicable split factors in the lag window.</summary>
    public required decimal CumulativeSplitFactor { get; init; }

    /// <summary>Individual split events that contributed to the cumulative factor.</summary>
    public required IReadOnlyList<SplitEvent> AppliedSplits { get; init; }

    public string? Warning { get; init; }
}
