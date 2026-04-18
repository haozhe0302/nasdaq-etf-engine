namespace Hqqq.Domain.Entities;

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
