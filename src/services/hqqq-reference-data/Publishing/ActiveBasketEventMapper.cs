using Hqqq.Contracts.Events;
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
/// quote-engine checks before accepting the message.
/// </remarks>
public static class ActiveBasketEventMapper
{
    public static BasketActiveStateV1 ToEvent(ActiveBasket active)
    {
        ArgumentNullException.ThrowIfNull(active);

        var snapshot = active.Snapshot;
        var sharesOrigin = snapshot.Source; // e.g. "live:file", "fallback-seed"

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
        };
    }
}
