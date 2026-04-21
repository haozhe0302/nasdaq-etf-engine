namespace Hqqq.ReferenceData.CorporateActions.Contracts;

/// <summary>
/// Stock-split event used by the Phase-2-native adjustment layer. The
/// multiplicative <see cref="Factor"/> is applied to a constituent's
/// <c>SharesHeld</c> when <see cref="EffectiveDate"/> falls inside the
/// adjustment window <c>(snapshot.AsOfDate, runtimeDate]</c>.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>Forward 4:1 split → <c>Factor = 4m</c>.</item>
///   <item>Reverse 1:10 split → <c>Factor = 0.1m</c>.</item>
/// </list>
/// </remarks>
public sealed record SplitEvent
{
    /// <summary>Ticker symbol (upper-case).</summary>
    public required string Symbol { get; init; }

    /// <summary>Date the split takes effect (first trading day at the new ratio).</summary>
    public required DateOnly EffectiveDate { get; init; }

    /// <summary>Multiplicative factor applied to share counts.</summary>
    public required decimal Factor { get; init; }

    /// <summary>Human-readable description (e.g. "4:1 forward split").</summary>
    public string? Description { get; init; }

    /// <summary>Data source that reported this event (e.g. <c>"file"</c>, <c>"tiingo"</c>).</summary>
    public required string Source { get; init; }
}
