using Hqqq.Domain.Services;
using Hqqq.Domain.ValueObjects;
using Hqqq.ReferenceData.CorporateActions.Contracts;
using Hqqq.ReferenceData.Services;
using Hqqq.ReferenceData.Sources;

namespace Hqqq.ReferenceData.CorporateActions.Services;

/// <summary>
/// Detects basket transitions (symbols added / removed relative to the
/// previous active basket) and re-calibrates the <c>ScaleFactor</c> for
/// iNAV continuity using <see cref="ScaleFactorCalibrator"/> from the
/// shared domain. Stateless — callers pass the previous basket + new
/// snapshot and receive a new snapshot plus a transition report.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>First activation (no previous basket): no transition, report
///         has empty added/removed lists and the snapshot is returned
///         unchanged.</item>
///   <item>Symbols added / removed: detected by set diff on
///         upper-cased tickers.</item>
///   <item>Scale-factor continuity: when any symbol changes (add, remove,
///         or even a pure split) the new raw value differs from the old
///         raw value, so we call
///         <c>ScaleFactorCalibrator.RecalibrateForContinuity</c> and set
///         <see cref="AdjustmentReport.ScaleFactorRecalibrated"/>.</item>
/// </list>
/// </remarks>
public sealed class BasketTransitionPlanner
{
    /// <summary>
    /// Computes transition + continuity metadata and returns a snapshot
    /// whose <c>ScaleFactor</c> has been re-calibrated when the
    /// pricing-basis raw value changed.
    /// </summary>
    /// <param name="previous">Previously active basket, or <c>null</c> on first activation.</param>
    /// <param name="snapshot">New (already corp-action adjusted) snapshot.</param>
    /// <param name="baseline">Initial adjustment report from the corp-action layer (splits + renames).</param>
    /// <returns>
    /// Updated snapshot (same or recalibrated <c>ScaleFactor</c>) and a
    /// transition-enriched <see cref="AdjustmentReport"/>.
    /// </returns>
    public (HoldingsSnapshot Snapshot, AdjustmentReport Report) Plan(
        ActiveBasket? previous,
        HoldingsSnapshot snapshot,
        AdjustmentReport baseline)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(baseline);

        if (previous is null)
        {
            return (snapshot, baseline);
        }

        var prev = previous.Snapshot;

        var prevSymbols = prev.Constituents
            .Select(c => c.Symbol.ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);
        var newSymbols = snapshot.Constituents
            .Select(c => c.Symbol.ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);

        var added = newSymbols.Except(prevSymbols, StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal).ToArray();
        var removed = prevSymbols.Except(newSymbols, StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal).ToArray();

        var prevRawValue = ComputeRawValue(prev);
        var newRawValue = ComputeRawValue(snapshot);

        HoldingsSnapshot outSnapshot = snapshot;
        bool recalibrated = false;
        decimal? newScale = null;

        // Only recalibrate when the raw value genuinely shifted — e.g.
        // when constituents changed or split factors changed share
        // counts. An identical raw value means the pricing basis is
        // continuous and we keep the fresh snapshot's scale as-is.
        if (prev.ScaleFactor > 0m
            && prevRawValue > 0m
            && newRawValue > 0m
            && prevRawValue != newRawValue)
        {
            var recalibratedScale = ScaleFactorCalibrator.RecalibrateForContinuity(
                oldScale: new ScaleFactor(prev.ScaleFactor),
                oldRawValue: prevRawValue,
                newRawValue: newRawValue);

            if (recalibratedScale.IsInitialized)
            {
                outSnapshot = snapshot with { ScaleFactor = recalibratedScale.Value };
                recalibrated = true;
                newScale = recalibratedScale.Value;
            }
        }

        var enriched = baseline with
        {
            AddedSymbols = added,
            RemovedSymbols = removed,
            ScaleFactorRecalibrated = recalibrated,
            PreviousScaleFactor = prev.ScaleFactor,
            NewScaleFactor = recalibrated ? newScale : snapshot.ScaleFactor,
        };

        return (outSnapshot, enriched);
    }

    /// <summary>
    /// Sum of <c>sharesHeld * referencePrice</c> across the constituent
    /// rows. Mirrors the domain's raw-basket value used by
    /// <see cref="ScaleFactorCalibrator"/>.
    /// </summary>
    private static decimal ComputeRawValue(HoldingsSnapshot snapshot)
    {
        decimal total = 0m;
        foreach (var c in snapshot.Constituents)
        {
            if (c.SharesHeld > 0m && c.ReferencePrice > 0m)
            {
                total += c.SharesHeld * c.ReferencePrice;
            }
        }
        return total;
    }
}
