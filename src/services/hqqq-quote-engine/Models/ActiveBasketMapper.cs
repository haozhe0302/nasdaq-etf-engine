using Hqqq.Contracts.Events;
using Hqqq.Domain.Entities;
using Hqqq.Domain.ValueObjects;

namespace Hqqq.QuoteEngine.Models;

/// <summary>
/// Translates the wire-level <see cref="BasketActiveStateV1"/> event into the
/// engine-internal <see cref="ActiveBasket"/>. Shared between the live Kafka
/// consumer and the checkpoint restorer so basket activation always goes
/// through a single, identical mapping.
/// </summary>
public static class ActiveBasketMapper
{
    public static ActiveBasket ToActiveBasket(BasketActiveStateV1 state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var constituents = new List<BasketConstituentState>(state.Constituents.Count);
        foreach (var c in state.Constituents)
        {
            constituents.Add(new BasketConstituentState
            {
                Symbol = c.Symbol,
                SecurityName = c.SecurityName,
                Sector = c.Sector,
                TargetWeight = c.TargetWeight,
                SharesHeld = c.SharesHeld,
                SharesOrigin = c.SharesOrigin,
            });
        }

        var entries = new List<PricingBasisEntry>(state.PricingBasis.Entries.Count);
        foreach (var e in state.PricingBasis.Entries)
        {
            entries.Add(new PricingBasisEntry
            {
                Symbol = e.Symbol,
                Shares = e.Shares,
                ReferencePrice = e.ReferencePrice,
                SharesOrigin = e.SharesOrigin,
                TargetWeight = e.TargetWeight,
            });
        }

        var basis = new PricingBasis
        {
            BasketFingerprint = state.Fingerprint,
            PricingBasisFingerprint = state.PricingBasis.PricingBasisFingerprint,
            CreatedAtUtc = state.PricingBasis.CreatedAtUtc,
            Entries = entries,
            InferredTotalNotional = state.PricingBasis.InferredTotalNotional,
            OfficialSharesCount = state.PricingBasis.OfficialSharesCount,
            DerivedSharesCount = state.PricingBasis.DerivedSharesCount,
        };

        return new ActiveBasket
        {
            BasketId = state.BasketId,
            Fingerprint = state.Fingerprint,
            AsOfDate = state.AsOfDate,
            ActivatedAtUtc = state.ActivatedAtUtc,
            Constituents = constituents,
            PricingBasis = basis,
            ScaleFactor = new ScaleFactor(state.ScaleFactor),
            NavPreviousClose = state.NavPreviousClose,
            QqqPreviousClose = state.QqqPreviousClose,
        };
    }

    public static BasketActiveStateV1 ToEvent(ActiveBasket basket)
    {
        ArgumentNullException.ThrowIfNull(basket);

        var constituents = new List<BasketConstituentV1>(basket.Constituents.Count);
        foreach (var c in basket.Constituents)
        {
            constituents.Add(new BasketConstituentV1
            {
                Symbol = c.Symbol,
                SecurityName = c.SecurityName,
                Sector = c.Sector,
                TargetWeight = c.TargetWeight,
                SharesHeld = c.SharesHeld,
                SharesOrigin = c.SharesOrigin,
            });
        }

        var entries = new List<PricingBasisEntryV1>(basket.PricingBasis.Entries.Count);
        foreach (var e in basket.PricingBasis.Entries)
        {
            entries.Add(new PricingBasisEntryV1
            {
                Symbol = e.Symbol,
                Shares = e.Shares,
                ReferencePrice = e.ReferencePrice,
                SharesOrigin = e.SharesOrigin,
                TargetWeight = e.TargetWeight,
            });
        }

        return new BasketActiveStateV1
        {
            BasketId = basket.BasketId,
            Fingerprint = basket.Fingerprint,
            Version = basket.Fingerprint,
            AsOfDate = basket.AsOfDate,
            ActivatedAtUtc = basket.ActivatedAtUtc,
            Constituents = constituents,
            PricingBasis = new PricingBasisV1
            {
                PricingBasisFingerprint = basket.PricingBasis.PricingBasisFingerprint,
                CreatedAtUtc = basket.PricingBasis.CreatedAtUtc,
                Entries = entries,
                InferredTotalNotional = basket.PricingBasis.InferredTotalNotional,
                OfficialSharesCount = basket.PricingBasis.OfficialSharesCount,
                DerivedSharesCount = basket.PricingBasis.DerivedSharesCount,
            },
            ScaleFactor = basket.ScaleFactor.Value,
            NavPreviousClose = basket.NavPreviousClose,
            QqqPreviousClose = basket.QqqPreviousClose,
        };
    }
}
