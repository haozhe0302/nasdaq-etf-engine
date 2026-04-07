namespace Hqqq.Api.Modules.CorporateActions.Contracts;

/// <summary>
/// Full audit trail of a corporate-action adjustment pass over a basket snapshot.
/// </summary>
public sealed record AdjustmentReport
{
    public required int AdjustedConstituentCount { get; init; }
    public required int UnadjustedConstituentCount { get; init; }

    /// <summary>Details for each constituent that was actually adjusted (non-trivial factor).</summary>
    public required IReadOnlyList<ConstituentAdjustment> Adjustments { get; init; }

    public required DateOnly BasketAsOfDate { get; init; }
    public required DateOnly RuntimeDate { get; init; }

    /// <summary>True when the provider threw and the service fell back to unadjusted shares.</summary>
    public required bool ProviderFailed { get; init; }

    public string? ProviderError { get; init; }
    public required DateTimeOffset ComputedAtUtc { get; init; }
}
