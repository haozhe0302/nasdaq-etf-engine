namespace Hqqq.Api.Modules.Pricing.Contracts;

/// <summary>
/// Persisted calibration state that anchors the iNAV calculation to a known
/// scale factor for a specific basket version. Includes the pricing-basis
/// entries so that the quantity vector survives process restarts.
/// </summary>
public sealed record ScaleState
{
    public static readonly ScaleState Uninitialized = new()
    {
        ScaleFactor = 0m,
        BasketFingerprint = "",
        PricingBasisFingerprint = "",
        ActivatedAtUtc = DateTimeOffset.MinValue,
        ComputedAtUtc = DateTimeOffset.MinValue,
        IsInitialized = false,
        InferredTotalNotional = 0m,
        BasisEntries = [],
    };

    public required decimal ScaleFactor { get; init; }
    public required string BasketFingerprint { get; init; } = "";
    public required string PricingBasisFingerprint { get; init; } = "";
    public required DateTimeOffset ActivatedAtUtc { get; init; }
    public required DateTimeOffset ComputedAtUtc { get; init; }
    public bool IsInitialized { get; init; } = true;
    public required decimal InferredTotalNotional { get; init; }
    public IReadOnlyList<PricingBasisEntry> BasisEntries { get; init; } = [];
}
