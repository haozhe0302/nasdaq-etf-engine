namespace Hqqq.Api.Modules.Pricing.Contracts;

/// <summary>
/// Persisted calibration state that anchors the iNAV calculation to a known
/// reference NAV and creation-unit size. An uninitialized state signals that
/// the engine has not yet completed its first calibration.
/// </summary>
public sealed record ScaleState
{
    public static readonly ScaleState Uninitialized = new()
    {
        ScaleFactor = 0m,
        BaseNav = 0m,
        SharesPerCreationUnit = 0m,
        ComputedAtUtc = DateTimeOffset.MinValue,
        IsInitialized = false,
    };

    public required decimal ScaleFactor { get; init; }
    public required decimal BaseNav { get; init; }
    public required decimal SharesPerCreationUnit { get; init; }
    public required DateTimeOffset ComputedAtUtc { get; init; }
    public bool IsInitialized { get; init; } = true;
}
