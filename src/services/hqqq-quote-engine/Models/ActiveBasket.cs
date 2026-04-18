using Hqqq.Domain.Entities;
using Hqqq.Domain.ValueObjects;

namespace Hqqq.QuoteEngine.Models;

/// <summary>
/// Fully-resolved basket input delivered through <c>IBasketStateFeed</c>:
/// basket identity + constituent metadata + pre-computed pricing basis +
/// scale factor. In B2 this arrives from an in-memory fake; in B3 it will
/// be assembled by a Kafka consumer subscribed to
/// <c>refdata.basket.active.v1</c> + reference-data lookups.
/// </summary>
public sealed record ActiveBasket
{
    public required string BasketId { get; init; }
    public required string Fingerprint { get; init; }
    public required DateOnly AsOfDate { get; init; }
    public required DateTimeOffset ActivatedAtUtc { get; init; }

    public required IReadOnlyList<BasketConstituentState> Constituents { get; init; }
    public required PricingBasis PricingBasis { get; init; }
    public required ScaleFactor ScaleFactor { get; init; }

    /// <summary>
    /// Reference price used to anchor change-percent calculations when no
    /// per-symbol previous-close is available for the whole basket.
    /// </summary>
    public decimal? NavPreviousClose { get; init; }

    public decimal? QqqPreviousClose { get; init; }
}
