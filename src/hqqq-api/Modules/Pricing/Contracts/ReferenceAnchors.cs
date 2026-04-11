namespace Hqqq.Api.Modules.Pricing.Contracts;

/// <summary>
/// Previous-close anchors used for day-over-day change % calculations.
/// Refreshed daily by <see cref="Services.ReferenceAnchorsRefreshService"/>.
/// </summary>
public sealed record ReferenceAnchors
{
    /// <summary>QQQ's previous trading-day close from Tiingo REST.</summary>
    public decimal? QqqPreviousClose { get; init; }

    /// <summary>HQQQ iNAV at the end of the previous trading day (from History).</summary>
    public decimal? NavPreviousClose { get; init; }

    /// <summary>The ET trading day these anchors are computed for (i.e. "today").</summary>
    public required DateOnly AnchorDate { get; init; }

    /// <summary>UTC timestamp of the last successful refresh.</summary>
    public required DateTimeOffset RefreshedAtUtc { get; init; }
}
