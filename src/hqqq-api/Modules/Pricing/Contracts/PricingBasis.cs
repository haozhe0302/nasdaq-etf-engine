namespace Hqqq.Api.Modules.Pricing.Contracts;

/// <summary>
/// A basket-version-specific quantity vector used for raw basket valuation.
/// Built once per basket activation, then held stable until the next activation.
/// </summary>
public sealed record PricingBasis
{
    public required string BasketFingerprint { get; init; }
    public required string PricingBasisFingerprint { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required IReadOnlyList<PricingBasisEntry> Entries { get; init; }
    public required decimal InferredTotalNotional { get; init; }
    public required int OfficialSharesCount { get; init; }
    public required int DerivedSharesCount { get; init; }
}

/// <summary>
/// One row of the pricing-basis quantity vector.
/// </summary>
public sealed record PricingBasisEntry
{
    public required string Symbol { get; init; }
    public required int Shares { get; init; }
    public required decimal ReferencePrice { get; init; }

    /// <summary>"official" (disclosed shares from public source) or "derived" (inferred from target weight).</summary>
    public required string SharesOrigin { get; init; }

    /// <summary>Target weight as a fraction (e.g. 0.125 for 12.5%).</summary>
    public decimal? TargetWeight { get; init; }
}
