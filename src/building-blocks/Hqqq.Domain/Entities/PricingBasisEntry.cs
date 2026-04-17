namespace Hqqq.Domain.Entities;

/// <summary>
/// One row of the pricing-basis quantity vector.
/// Ported from the legacy <c>Hqqq.Api.Modules.Pricing.Contracts.PricingBasisEntry</c>.
/// </summary>
public sealed record PricingBasisEntry
{
    public required string Symbol { get; init; }
    public required int Shares { get; init; }
    public required decimal ReferencePrice { get; init; }

    /// <summary>
    /// "official" (disclosed shares from public source) or "derived" (inferred from target weight).
    /// </summary>
    public required string SharesOrigin { get; init; }

    /// <summary>Target weight as a fraction (e.g. 0.125 for 12.5%).</summary>
    public decimal? TargetWeight { get; init; }
}
