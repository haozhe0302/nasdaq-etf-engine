using Hqqq.Contracts.Dtos;
using Hqqq.QuoteEngine.Abstractions;
using Hqqq.QuoteEngine.Models;
using Hqqq.QuoteEngine.State;

namespace Hqqq.QuoteEngine.Services;

/// <summary>
/// Builds the slim realtime <see cref="QuoteUpdateDto"/> delta. Reuses the
/// snapshot materializer's freshness / feeds / movers builders so that the
/// two outputs never drift — they are literally the same field derivations
/// minus the full series and plus the single latest series point.
/// </summary>
public sealed class QuoteDeltaMaterializer
{
    private readonly BasketStateStore _baskets;
    private readonly EngineRuntimeState _runtime;
    private readonly SnapshotMaterializer _snapshotMaterializer;
    private readonly ISystemClock _clock;

    public QuoteDeltaMaterializer(
        BasketStateStore baskets,
        EngineRuntimeState runtime,
        SnapshotMaterializer snapshotMaterializer,
        ISystemClock clock)
    {
        _baskets = baskets;
        _runtime = runtime;
        _snapshotMaterializer = snapshotMaterializer;
        _clock = clock;
    }

    public QuoteUpdateDto? Build()
    {
        var basket = _baskets.Current;
        if (basket is null) return null;

        var readiness = _runtime.Readiness;
        var freshness = _snapshotMaterializer.BuildFreshness(basket);
        var feeds = _snapshotMaterializer.BuildFeeds(basket, readiness);
        var movers = _snapshotMaterializer.BuildMovers(basket);
        var latestPoint = _runtime.TakeLatestSeriesPoint();
        var asOf = _runtime.LastNavCalcUtc ?? _clock.UtcNow;

        return new QuoteUpdateDto
        {
            Nav = _runtime.Nav,
            NavChangePct = _runtime.NavChangePct,
            MarketPrice = _runtime.MarketPrice,
            PremiumDiscountPct = _runtime.PremiumDiscountPct,
            Qqq = _runtime.Qqq,
            QqqChangePct = _runtime.QqqChangePct,
            BasketValueB = _runtime.BasketValueB,
            AsOf = asOf,
            LatestSeriesPoint = latestPoint,
            Movers = movers,
            Freshness = freshness,
            Feeds = feeds,
            QuoteState = readiness.ToWireValue(),
            IsLive = readiness == QuoteReadiness.Live,
            IsFrozen = readiness == QuoteReadiness.FrozenAllStale,
            PauseReason = _runtime.PauseReason,
        };
    }
}
