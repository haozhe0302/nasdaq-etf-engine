namespace Hqqq.Persistence.Persistence;

/// <summary>
/// Database-shape representation of a single row in the
/// <c>quote_snapshots</c> hypertable. <see cref="Ts"/> is always normalized
/// to UTC at the mapping boundary so downstream SQL never sees a local-time
/// <see cref="DateTimeOffset"/>.
/// </summary>
public sealed record QuoteSnapshotRow
{
    public required string BasketId { get; init; }
    public required DateTimeOffset Ts { get; init; }
    public required decimal Nav { get; init; }
    public required decimal MarketProxyPrice { get; init; }
    public required decimal PremiumDiscountPct { get; init; }
    public required int StaleCount { get; init; }
    public required int FreshCount { get; init; }
    public required double MaxComponentAgeMs { get; init; }
    public required string QuoteQuality { get; init; }
}
