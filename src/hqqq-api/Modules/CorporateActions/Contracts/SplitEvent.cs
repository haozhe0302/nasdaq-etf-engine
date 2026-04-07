namespace Hqqq.Api.Modules.CorporateActions.Contracts;

/// <summary>
/// A single stock-split event for a constituent symbol.
/// </summary>
public sealed record SplitEvent
{
    /// <summary>Ticker symbol (upper-case).</summary>
    public required string Symbol { get; init; }

    /// <summary>Date the split takes effect (first trading day at the new ratio).</summary>
    public required DateOnly EffectiveDate { get; init; }

    /// <summary>
    /// Multiplicative factor applied to share counts.
    /// Forward 4:1 split → 4.0; reverse 1:4 → 0.25.
    /// </summary>
    public required decimal Factor { get; init; }

    /// <summary>Human-readable description (e.g. "4:1 forward split").</summary>
    public string? Description { get; init; }

    /// <summary>Data source that reported this event (e.g. "tiingo").</summary>
    public required string Source { get; init; }
}
