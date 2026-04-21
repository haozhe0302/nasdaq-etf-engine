namespace Hqqq.Contracts.Events;

/// <summary>
/// Fully-materialized active-basket payload published to the (compacted)
/// <c>refdata.basket.active.v1</c> topic. Carries everything the pricing
/// engine needs to activate a basket without a synchronous callback to
/// reference-data: constituents, pricing basis (quantity vector), and
/// calibrated scale factor.
///
/// This is the richer sibling of <see cref="BasketActivatedV1"/>. The slim
/// V1 record is reserved for lifecycle signals on <c>refdata.basket.events.v1</c>.
///
/// Key: <see cref="BasketId"/>.
/// </summary>
public sealed record BasketActiveStateV1
{
    public required string BasketId { get; init; }
    public required string Fingerprint { get; init; }
    public required string Version { get; init; }
    public required DateOnly AsOfDate { get; init; }
    public required DateTimeOffset ActivatedAtUtc { get; init; }

    public required IReadOnlyList<BasketConstituentV1> Constituents { get; init; }
    public required PricingBasisV1 PricingBasis { get; init; }

    /// <summary>
    /// Calibrated scale factor for the NAV formula. Values <c>&lt;= 0</c> are
    /// rejected by the consumer (treated as uninitialized).
    /// </summary>
    public required decimal ScaleFactor { get; init; }

    /// <summary>Reference NAV previous close, used to anchor change-percent.</summary>
    public decimal? NavPreviousClose { get; init; }

    /// <summary>Reference anchor previous close (e.g. QQQ).</summary>
    public decimal? QqqPreviousClose { get; init; }

    /// <summary>
    /// Lineage tag for the basket payload (e.g. <c>"live:file"</c>,
    /// <c>"live:http"</c>, <c>"fallback-seed"</c>). Additive: existing
    /// historical messages on the compacted topic that lack this field
    /// deserialize to <c>"unknown"</c>.
    /// </summary>
    public string Source { get; init; } = "unknown";

    /// <summary>
    /// Number of constituents in the published payload. Convenience for
    /// downstream consumers; equal to <c>Constituents.Count</c>.
    /// </summary>
    public int ConstituentCount { get; init; }
}

/// <summary>Constituent metadata carried inline on the active-basket event.</summary>
public sealed record BasketConstituentV1
{
    public required string Symbol { get; init; }
    public required string SecurityName { get; init; }
    public required string Sector { get; init; }
    public decimal? TargetWeight { get; init; }
    public required decimal SharesHeld { get; init; }
    public required string SharesOrigin { get; init; }
}

/// <summary>Pricing-basis quantity vector carried inline on the active-basket event.</summary>
public sealed record PricingBasisV1
{
    public required string PricingBasisFingerprint { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required IReadOnlyList<PricingBasisEntryV1> Entries { get; init; }
    public required decimal InferredTotalNotional { get; init; }
    public required int OfficialSharesCount { get; init; }
    public required int DerivedSharesCount { get; init; }
}

/// <summary>One row of the pricing-basis quantity vector.</summary>
public sealed record PricingBasisEntryV1
{
    public required string Symbol { get; init; }
    public required int Shares { get; init; }
    public required decimal ReferencePrice { get; init; }
    public required string SharesOrigin { get; init; }
    public decimal? TargetWeight { get; init; }
}
