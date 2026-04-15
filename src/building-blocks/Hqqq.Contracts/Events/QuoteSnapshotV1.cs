namespace Hqqq.Contracts.Events;

/// <summary>
/// iNAV snapshot published to <c>pricing.snapshots.v1</c>
/// by the quote-engine on every compute cycle.
/// Key: <see cref="BasketId"/>.
/// </summary>
public sealed record QuoteSnapshotV1
{
    public required string BasketId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required decimal Nav { get; init; }
    public required decimal MarketProxyPrice { get; init; }
    public required decimal PremiumDiscountPct { get; init; }
    public required int StaleCount { get; init; }
    public required int FreshCount { get; init; }
    public required double MaxComponentAgeMs { get; init; }

    /// <summary>"live", "stale", or "frozen".</summary>
    public required string QuoteQuality { get; init; }
}
