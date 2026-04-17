using Hqqq.Contracts.Dtos;
using Hqqq.Contracts.Events;
using Hqqq.Domain.Services;
using Hqqq.QuoteEngine.Abstractions;
using Hqqq.QuoteEngine.Models;
using Hqqq.QuoteEngine.State;

namespace Hqqq.QuoteEngine.Services;

/// <summary>
/// Pure mapper from the engine's serving-shape <see cref="QuoteSnapshotDto"/>
/// to the Kafka <see cref="QuoteSnapshotV1"/> event. <see cref="QuoteSnapshotV1.MaxComponentAgeMs"/>
/// is computed from per-symbol observations because the serving DTO does not
/// carry it directly.
/// </summary>
public sealed class QuoteSnapshotV1Mapper
{
    private readonly PerSymbolQuoteStore _quotes;
    private readonly BasketStateStore _baskets;
    private readonly ISystemClock _clock;

    public QuoteSnapshotV1Mapper(
        PerSymbolQuoteStore quotes,
        BasketStateStore baskets,
        ISystemClock clock)
    {
        _quotes = quotes;
        _baskets = baskets;
        _clock = clock;
    }

    public QuoteSnapshotV1 Map(string basketId, QuoteSnapshotDto snapshot)
    {
        if (string.IsNullOrWhiteSpace(basketId))
            throw new ArgumentException("basketId must be non-empty", nameof(basketId));
        ArgumentNullException.ThrowIfNull(snapshot);

        var maxAgeMs = ComputeMaxComponentAgeMs();

        return new QuoteSnapshotV1
        {
            BasketId = basketId,
            Timestamp = snapshot.AsOf,
            Nav = snapshot.Nav,
            MarketProxyPrice = snapshot.MarketPrice,
            PremiumDiscountPct = snapshot.PremiumDiscountPct,
            StaleCount = snapshot.Freshness.SymbolsStale,
            FreshCount = snapshot.Freshness.SymbolsFresh,
            MaxComponentAgeMs = maxAgeMs,
            QuoteQuality = MapQuality(snapshot.QuoteState),
        };
    }

    private double ComputeMaxComponentAgeMs()
    {
        var basket = _baskets.Current;
        if (basket is null) return 0d;

        var now = _clock.UtcNow;
        double max = 0d;
        foreach (var entry in basket.PricingBasis.Entries)
        {
            var state = _quotes.Get(entry.Symbol);
            if (state is null) continue;
            var ageMs = (now - state.ReceivedAtUtc).TotalMilliseconds;
            if (ageMs > max) max = ageMs;
        }
        return Math.Round(max, 2);
    }

    private static string MapQuality(string quoteState) => quoteState switch
    {
        "live" => "live",
        "frozen_all_stale" => "frozen",
        _ => "stale",
    };
}
