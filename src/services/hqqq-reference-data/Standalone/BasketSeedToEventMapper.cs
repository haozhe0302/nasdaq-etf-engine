using Hqqq.Contracts.Events;

namespace Hqqq.ReferenceData.Standalone;

/// <summary>
/// Pure mapper that turns a validated <see cref="BasketSeed"/> into a
/// fully-populated <see cref="BasketActiveStateV1"/> wire event.
/// </summary>
/// <remarks>
/// The output is intentionally complete: <c>Constituents</c> and
/// <c>PricingBasis.Entries</c> are both populated and the scale factor
/// is positive so <c>BasketEventConsumer.HandleAsync</c> in quote-engine
/// accepts the message (it drops events with empty constituents/basis or
/// non-positive scale).
/// </remarks>
public static class BasketSeedToEventMapper
{
    public static BasketActiveStateV1 ToEvent(BasketSeed seed, DateTimeOffset activatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(seed);

        var constituents = seed.Constituents
            .Select(c => new BasketConstituentV1
            {
                Symbol = c.Symbol.ToUpperInvariant(),
                SecurityName = c.Name,
                Sector = c.Sector,
                TargetWeight = c.TargetWeight,
                SharesHeld = c.SharesHeld,
                SharesOrigin = "seed",
            })
            .ToArray();

        var entries = seed.Constituents
            .Select(c => new PricingBasisEntryV1
            {
                Symbol = c.Symbol.ToUpperInvariant(),
                // Basket seed shares are tracked as decimal; the wire
                // contract uses int (whole-share quantities are what
                // pricing engines typically receive from issuers). Round
                // down to keep the inferred notional <= the seed total.
                Shares = (int)Math.Floor(c.SharesHeld),
                ReferencePrice = c.ReferencePrice,
                SharesOrigin = "seed",
                TargetWeight = c.TargetWeight,
            })
            .ToArray();

        var inferredNotional = entries.Sum(e => e.ReferencePrice * e.Shares);

        var pricingBasis = new PricingBasisV1
        {
            PricingBasisFingerprint = seed.Fingerprint,
            CreatedAtUtc = activatedAtUtc,
            Entries = entries,
            InferredTotalNotional = inferredNotional,
            // The seed declares share counts directly; we mark them
            // "official" so downstream tooling doesn't show a "derived"
            // warning chip on the demo basket.
            OfficialSharesCount = entries.Length,
            DerivedSharesCount = 0,
        };

        return new BasketActiveStateV1
        {
            BasketId = seed.BasketId,
            Fingerprint = seed.Fingerprint,
            Version = seed.Version,
            AsOfDate = seed.AsOfDate,
            ActivatedAtUtc = activatedAtUtc,
            Constituents = constituents,
            PricingBasis = pricingBasis,
            ScaleFactor = seed.ScaleFactor,
            NavPreviousClose = seed.NavPreviousClose,
            QqqPreviousClose = seed.QqqPreviousClose,
        };
    }
}
