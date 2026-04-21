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
        var sharesOrigin = snapshot.Source;

        var constituents = snapshot.Constituents
            .Select(c => new BasketConstituentV1
            {
                Symbol = c.Symbol.ToUpperInvariant(),
                SecurityName = c.Name,
                Sector = c.Sector,
                TargetWeight = c.TargetWeight,
                SharesHeld = c.SharesHeld,
                SharesOrigin = sharesOrigin,
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
                SharesOrigin = sharesOrigin,
                TargetWeight = c.TargetWeight,
            })
            .ToArray();

        var inferredNotional = entries.Sum(e => e.ReferencePrice * e.Shares);

        var pricingBasis = new PricingBasisV1
        {
            PricingBasisFingerprint = active.Fingerprint,
            CreatedAtUtc = active.ActivatedAtUtc,
            Entries = entries,
            InferredTotalNotional = inferredNotional,
            OfficialSharesCount = entries.Length,
            DerivedSharesCount = 0,
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
