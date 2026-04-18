using Hqqq.Contracts.Events;

namespace Hqqq.Persistence.Persistence;

/// <summary>
/// Pure mapper from the wire-shape <see cref="QuoteSnapshotV1"/> event to
/// the database-shape <see cref="QuoteSnapshotRow"/>. Centralizes UTC
/// normalization of the timestamp so the writer can stay trivial.
/// </summary>
public static class QuoteSnapshotRowMapper
{
    public static QuoteSnapshotRow Map(QuoteSnapshotV1 snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(snapshot.BasketId))
            throw new ArgumentException("snapshot must have a non-empty BasketId", nameof(snapshot));

        return new QuoteSnapshotRow
        {
            BasketId = snapshot.BasketId,
            Ts = snapshot.Timestamp.ToUniversalTime(),
            Nav = snapshot.Nav,
            MarketProxyPrice = snapshot.MarketProxyPrice,
            PremiumDiscountPct = snapshot.PremiumDiscountPct,
            StaleCount = snapshot.StaleCount,
            FreshCount = snapshot.FreshCount,
            MaxComponentAgeMs = snapshot.MaxComponentAgeMs,
            QuoteQuality = snapshot.QuoteQuality,
        };
    }
}
