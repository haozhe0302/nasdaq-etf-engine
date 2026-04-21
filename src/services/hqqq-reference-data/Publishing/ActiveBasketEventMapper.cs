using Hqqq.Contracts.Events;
using Hqqq.ReferenceData.CorporateActions.Contracts;
using Hqqq.ReferenceData.Services;

namespace Hqqq.ReferenceData.Publishing;

/// <summary>
/// Pure mapper that turns an <see cref="ActiveBasket"/> (lineage-tagged
/// <see cref="Hqqq.ReferenceData.Sources.HoldingsSnapshot"/> + fingerprint
/// + activation timestamp) into a fully-populated
/// <see cref="BasketActiveStateV1"/> wire event.
/// </summary>
/// <remarks>
/// The output is intentionally complete: both <c>Constituents</c> and
/// <c>PricingBasis.Entries</c> are populated, and <c>ScaleFactor</c> is
/// positive — those are the invariants <c>BasketEventConsumer</c> in
/// quote-engine checks before accepting the message. When the refresh
/// pipeline passed a <see cref="AdjustmentReport"/> / previous basket,
/// the additive <c>AdjustmentSummary</c>, <c>PreviousBasketId</c>, and
/// <c>PreviousFingerprint</c> fields are populated too.
/// </remarks>
public static class ActiveBasketEventMapper
{
    public static BasketActiveStateV1 ToEvent(
        ActiveBasket active,
        ActiveBasket? previous = null,
        AdjustmentReport? report = null)
    {
        ArgumentNullException.ThrowIfNull(active);

        var snapshot = active.Snapshot;
        var snapshotSource = snapshot.Source;

        // Prefer the per-row SharesSource lineage set by the anchored
        // merge (e.g. "stockanalysis", "schwab", "unavailable"). Fall
        // back to the snapshot-level Source tag for legacy rows that
        // pre-date the Phase 1 port.
        var constituents = snapshot.Constituents
            .Select(c => new BasketConstituentV1
            {
                Symbol = c.Symbol.ToUpperInvariant(),
                SecurityName = c.Name,
                Sector = c.Sector,
                TargetWeight = c.TargetWeight,
                SharesHeld = c.SharesHeld,
                SharesOrigin = string.IsNullOrWhiteSpace(c.SharesSource)
                    ? snapshotSource
                    : c.SharesSource!,
            })
            .ToArray();

        var entries = snapshot.Constituents
            .Select(c => new PricingBasisEntryV1
            {
                Symbol = c.Symbol.ToUpperInvariant(),
                // Whole-share quantities are what pricing engines typically
                // receive from issuers; round down so the inferred notional
                // never overstates the seed.
                Shares = (int)Math.Floor(c.SharesHeld),
                ReferencePrice = c.ReferencePrice,
                SharesOrigin = string.IsNullOrWhiteSpace(c.SharesSource)
                    ? snapshotSource
                    : c.SharesSource!,
                TargetWeight = c.TargetWeight,
            })
            .ToArray();

        var inferredNotional = entries.Sum(e => e.ReferencePrice * e.Shares);

        // Official vs derived split: a row is "official" only when its
        // SharesSource is a named anchor (stockanalysis / schwab /
        // split-adjusted suffix). "unavailable" tail rows are not
        // counted as official so the quote-engine can reason about
        // pricing-basis confidence against meaningful basket state.
        var officialCount = snapshot.Constituents.Count(c =>
            c.SharesHeld > 0m
            && !string.IsNullOrWhiteSpace(c.SharesSource)
            && !string.Equals(c.SharesSource, "unavailable", StringComparison.OrdinalIgnoreCase)
            && !c.SharesSource.Contains("derived", StringComparison.OrdinalIgnoreCase));

        var pricingBasis = new PricingBasisV1
        {
            PricingBasisFingerprint = active.Fingerprint,
            CreatedAtUtc = active.ActivatedAtUtc,
            Entries = entries,
            InferredTotalNotional = inferredNotional,
            OfficialSharesCount = officialCount,
            DerivedSharesCount = Math.Max(0, entries.Length - officialCount),
        };

        AdjustmentSummaryV1? summary = null;
        if (report is not null)
        {
            summary = new AdjustmentSummaryV1
            {
                SplitsApplied = report.SplitsApplied,
                RenamesApplied = report.RenamesApplied,
                AddedSymbols = report.AddedSymbols,
                RemovedSymbols = report.RemovedSymbols,
                AdjustmentAsOfDate = report.RuntimeDate,
                AdjustmentAppliedAtUtc = report.AppliedAtUtc,
                ProviderSource = report.Source,
                ScaleFactorRecalibrated = report.ScaleFactorRecalibrated,
                Note = report.ProviderError,
            };
        }

        return new BasketActiveStateV1
        {
            BasketId = snapshot.BasketId,
            Fingerprint = active.Fingerprint,
            Version = snapshot.Version,
            AsOfDate = snapshot.AsOfDate,
            ActivatedAtUtc = active.ActivatedAtUtc,
            Constituents = constituents,
            PricingBasis = pricingBasis,
            ScaleFactor = snapshot.ScaleFactor,
            NavPreviousClose = snapshot.NavPreviousClose,
            QqqPreviousClose = snapshot.QqqPreviousClose,
            Source = snapshot.Source,
            ConstituentCount = constituents.Length,
            PreviousBasketId = previous?.Snapshot.BasketId,
            PreviousFingerprint = previous?.Fingerprint,
            AdjustmentSummary = summary,
        };
    }
}
