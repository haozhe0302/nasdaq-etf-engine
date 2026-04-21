namespace Hqqq.ReferenceData.CorporateActions.Contracts;

/// <summary>
/// Full audit trail of a corporate-action + transition pass over a
/// <c>HoldingsSnapshot</c>. Surfaced on <c>GET /api/basket/current</c>
/// and on the <c>BasketActiveStateV1.AdjustmentSummary</c> Kafka payload
/// (summarised) so downstream consumers can see what changed.
/// </summary>
public sealed record AdjustmentReport
{
    /// <summary>Per-constituent rows for constituents that actually received a split.</summary>
    public required IReadOnlyList<ConstituentAdjustment> SplitAdjustments { get; init; }

    /// <summary>Per-constituent rows for constituents whose ticker was renamed.</summary>
    public required IReadOnlyList<RenameAdjustment> RenameAdjustments { get; init; }

    /// <summary>Constituents that appeared in the new snapshot but not in the previous.</summary>
    public required IReadOnlyList<string> AddedSymbols { get; init; }

    /// <summary>Constituents that appeared in the previous snapshot but not in the new one.</summary>
    public required IReadOnlyList<string> RemovedSymbols { get; init; }

    public required DateOnly BasketAsOfDate { get; init; }
    public required DateOnly RuntimeDate { get; init; }
    public required DateTimeOffset AppliedAtUtc { get; init; }

    /// <summary>
    /// Lineage of the corporate-action provider feed used for this pass
    /// (mirrors <see cref="CorporateActionFeed.Source"/>).
    /// </summary>
    public required string Source { get; init; }

    /// <summary>True when the basket transition triggered a scale-factor re-calibration.</summary>
    public bool ScaleFactorRecalibrated { get; init; }

    public decimal? PreviousScaleFactor { get; init; }
    public decimal? NewScaleFactor { get; init; }

    /// <summary>Non-null when the provider fell back to a degraded path; surfaces the error for operators.</summary>
    public string? ProviderError { get; init; }

    /// <summary>Convenience: count of constituents with a split adjustment applied.</summary>
    public int SplitsApplied => SplitAdjustments.Count;

    /// <summary>Convenience: count of constituents with a rename applied.</summary>
    public int RenamesApplied => RenameAdjustments.Count;

    public static AdjustmentReport Empty(
        string source,
        DateOnly basketAsOfDate,
        DateOnly runtimeDate,
        DateTimeOffset appliedAtUtc,
        string? providerError = null) => new()
        {
            SplitAdjustments = Array.Empty<ConstituentAdjustment>(),
            RenameAdjustments = Array.Empty<RenameAdjustment>(),
            AddedSymbols = Array.Empty<string>(),
            RemovedSymbols = Array.Empty<string>(),
            BasketAsOfDate = basketAsOfDate,
            RuntimeDate = runtimeDate,
            AppliedAtUtc = appliedAtUtc,
            Source = source,
            ProviderError = providerError,
        };
}

/// <summary>Per-constituent record of a split adjustment that was applied.</summary>
public sealed record ConstituentAdjustment
{
    public required string Symbol { get; init; }
    public required decimal OriginalShares { get; init; }
    public required decimal AdjustedShares { get; init; }

    /// <summary>Product of all applicable split factors in the adjustment window.</summary>
    public required decimal CumulativeFactor { get; init; }

    /// <summary>Individual split events that contributed to the cumulative factor.</summary>
    public required IReadOnlyList<SplitEvent> AppliedSplits { get; init; }
}

/// <summary>Per-constituent record of a rename that was applied.</summary>
public sealed record RenameAdjustment
{
    public required string OldSymbol { get; init; }
    public required string NewSymbol { get; init; }
    public required IReadOnlyList<SymbolRenameEvent> AppliedRenames { get; init; }
}
