namespace Hqqq.ReferenceData.CorporateActions.Contracts;

/// <summary>
/// Normalized result of a <see cref="ICorporateActionProvider.FetchAsync"/>
/// call. Carries the split + rename events for the requested window plus
/// provenance so the adjustment layer can report where the data came from
/// and whether any upstream lookup was degraded.
/// </summary>
public sealed record CorporateActionFeed
{
    public required IReadOnlyList<SplitEvent> Splits { get; init; }
    public required IReadOnlyList<SymbolRenameEvent> Renames { get; init; }

    /// <summary>
    /// Lineage tag for the feed (e.g. <c>"file"</c>, <c>"file+tiingo"</c>,
    /// <c>"file+tiingo-degraded"</c>). Surfaced on
    /// <see cref="AdjustmentReport.Source"/> and the Kafka
    /// <c>BasketActiveStateV1.AdjustmentSummary.ProviderSource</c>.
    /// </summary>
    public required string Source { get; init; }

    public required DateTimeOffset FetchedAtUtc { get; init; }

    /// <summary>Non-null when a provider threw / returned an error that the composite masked.</summary>
    public string? Error { get; init; }

    public static CorporateActionFeed Empty(string source) => new()
    {
        Splits = Array.Empty<SplitEvent>(),
        Renames = Array.Empty<SymbolRenameEvent>(),
        Source = source,
        FetchedAtUtc = DateTimeOffset.UtcNow,
    };
}
