using Hqqq.Domain.ValueObjects;

namespace Hqqq.Domain.Services;

/// <summary>
/// Pure calibration math for the iNAV scale factor. No IO, no state.
/// <para>
/// <see cref="Calibrate"/> is the bootstrap path: anchor the synthetic NAV
/// to a reference price (e.g. QQQ) so that <c>scale × rawValue ≈ anchor</c>.
/// </para>
/// <para>
/// <see cref="RecalibrateForContinuity"/> is the basket-activation path: pick
/// a new scale that keeps the NAV value continuous across a basis swap,
/// preserving <c>oldScale × oldRawValue == newScale × newRawValue</c>.
/// </para>
/// </summary>
public static class ScaleFactorCalibrator
{
    /// <summary>
    /// Bootstrap calibration: <c>scale = anchorPrice / rawValue</c>.
    /// Returns <see cref="ScaleFactor.Uninitialized"/> if either input is non-positive.
    /// </summary>
    public static ScaleFactor Calibrate(decimal anchorPrice, decimal rawValue)
    {
        if (anchorPrice <= 0m || rawValue <= 0m)
            return ScaleFactor.Uninitialized;

        return new ScaleFactor(anchorPrice / rawValue);
    }

    /// <summary>
    /// Continuity-preserving re-calibration used on basket activation.
    /// Chooses a new scale factor such that NAV is unchanged the instant
    /// the new basis takes over:
    /// <code>newScale = (oldScale × oldRawValue) / newRawValue</code>.
    /// Returns <see cref="ScaleFactor.Uninitialized"/> if any input is non-positive.
    /// </summary>
    public static ScaleFactor RecalibrateForContinuity(
        ScaleFactor oldScale,
        decimal oldRawValue,
        decimal newRawValue)
    {
        if (!oldScale.IsInitialized || oldRawValue <= 0m || newRawValue <= 0m)
            return ScaleFactor.Uninitialized;

        var oldNav = oldScale.Value * oldRawValue;
        if (oldNav <= 0m) return ScaleFactor.Uninitialized;

        return new ScaleFactor(oldNav / newRawValue);
    }

    /// <summary>
    /// Basis-points discontinuity that a naive basis swap *would* have introduced
    /// if we had NOT recalibrated — useful for operational telemetry.
    /// </summary>
    public static double ComputeActivationJumpBps(
        ScaleFactor oldScale,
        decimal oldRawValue,
        decimal newRawValue)
    {
        if (!oldScale.IsInitialized || oldRawValue <= 0m || newRawValue <= 0m)
            return 0d;

        var oldNav = oldScale.Value * oldRawValue;
        if (oldNav <= 0m) return 0d;

        var preRecalibrationNav = oldScale.Value * newRawValue;
        return (double)Math.Abs((preRecalibrationNav - oldNav) / oldNav * 10000m);
    }
}
