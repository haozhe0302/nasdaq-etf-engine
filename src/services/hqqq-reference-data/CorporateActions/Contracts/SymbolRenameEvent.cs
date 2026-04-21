namespace Hqqq.ReferenceData.CorporateActions.Contracts;

/// <summary>
/// Ticker rename (symbol remap) event used by the Phase-2-native
/// adjustment layer. When <see cref="EffectiveDate"/> falls inside the
/// adjustment window, the constituent identified by <see cref="OldSymbol"/>
/// has its <c>Symbol</c> replaced by <see cref="NewSymbol"/> before the
/// basket is fingerprinted / published. Ingress picks up the new symbol
/// on the next basket event.
/// </summary>
/// <remarks>
/// Scope is intentionally narrow: equity-identifier rename only. Cross-
/// exchange moves and ISIN/CUSIP-level remaps are not supported. Chained
/// renames within a single refresh window (A→B→C) are resolved by
/// <c>SymbolRemapResolver</c>.
/// </remarks>
public sealed record SymbolRenameEvent
{
    /// <summary>Former ticker (upper-case). Matched against the current snapshot's constituent symbols.</summary>
    public required string OldSymbol { get; init; }

    /// <summary>New ticker (upper-case). Written into the adjusted snapshot.</summary>
    public required string NewSymbol { get; init; }

    /// <summary>Date the rename takes effect.</summary>
    public required DateOnly EffectiveDate { get; init; }

    /// <summary>Human-readable description (e.g. "FB → META").</summary>
    public string? Description { get; init; }

    /// <summary>Data source that reported this event.</summary>
    public required string Source { get; init; }
}
