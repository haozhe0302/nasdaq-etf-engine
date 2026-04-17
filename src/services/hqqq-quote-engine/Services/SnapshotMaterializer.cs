using Hqqq.Contracts.Dtos;
using Hqqq.Domain.Services;
using Hqqq.QuoteEngine.Abstractions;
using Hqqq.QuoteEngine.Models;
using Hqqq.QuoteEngine.State;

namespace Hqqq.QuoteEngine.Services;

/// <summary>
/// Builds a full <see cref="QuoteSnapshotDto"/> from the current engine
/// state. Shape is intentionally aligned with the frontend's
/// <c>BQuoteSnapshot</c> adapter so the gateway can serve this directly
/// in B3.
/// </summary>
public sealed class SnapshotMaterializer
{
    private readonly PerSymbolQuoteStore _quotes;
    private readonly BasketStateStore _baskets;
    private readonly EngineRuntimeState _runtime;
    private readonly ISystemClock _clock;
    private readonly QuoteEngineOptions _options;

    public SnapshotMaterializer(
        PerSymbolQuoteStore quotes,
        BasketStateStore baskets,
        EngineRuntimeState runtime,
        ISystemClock clock,
        QuoteEngineOptions options)
    {
        _quotes = quotes;
        _baskets = baskets;
        _runtime = runtime;
        _clock = clock;
        _options = options;
    }

    public QuoteSnapshotDto? Build()
    {
        var basket = _baskets.Current;
        if (basket is null) return null;

        var readiness = _runtime.Readiness;
        var freshness = BuildFreshness(basket);
        var feeds = BuildFeeds(basket, readiness);
        var movers = BuildMovers(basket);
        var series = _runtime.GetSeries();

        var asOf = _runtime.LastNavCalcUtc ?? _clock.UtcNow;

        return new QuoteSnapshotDto
        {
            Nav = _runtime.Nav,
            NavChangePct = _runtime.NavChangePct,
            MarketPrice = _runtime.MarketPrice,
            PremiumDiscountPct = _runtime.PremiumDiscountPct,
            Qqq = _runtime.Qqq,
            QqqChangePct = _runtime.QqqChangePct,
            BasketValueB = _runtime.BasketValueB,
            AsOf = asOf,
            Series = series,
            Movers = movers,
            Freshness = freshness,
            Feeds = feeds,
            QuoteState = readiness.ToWireValue(),
            IsLive = readiness == QuoteReadiness.Live,
            IsFrozen = readiness == QuoteReadiness.FrozenAllStale,
            PauseReason = _runtime.PauseReason,
        };
    }

    internal FreshnessDto BuildFreshness(ActiveBasket basket)
    {
        var trackedSymbols = basket.PricingBasis.Entries
            .Select(e => e.Symbol)
            .ToList();

        var summary = _quotes.BuildFreshnessSummary(trackedSymbols, _options.StaleAfter);

        return new FreshnessDto
        {
            SymbolsTotal = summary.SymbolsTotal,
            SymbolsFresh = summary.SymbolsFresh,
            SymbolsStale = summary.SymbolsStale,
            FreshPct = summary.FreshPct,
            LastTickUtc = summary.LastTickUtc,
            AvgTickIntervalMs = summary.AvgTickIntervalMs,
        };
    }

    internal FeedInfoDto BuildFeeds(ActiveBasket basket, QuoteReadiness readiness)
    {
        // B2: no real session service or upstream feed wiring yet. We
        // surface the fields the frontend expects, with conservative
        // defaults; B3 will plug in real values from the ingress /
        // reference-data services.
        return new FeedInfoDto
        {
            WebSocketConnected = false,
            FallbackActive = false,
            PricingActive = readiness != QuoteReadiness.Uninitialized,
            BasketState = basket is null ? "unavailable" : "active",
            PendingActivationBlocked = false,
            PendingBlockedReason = null,
            MarketSessionState = null,
            IsRegularSessionOpen = null,
            IsTradingDay = null,
            NextOpenUtc = null,
            SessionLabel = null,
        };
    }

    internal IReadOnlyList<MoverDto> BuildMovers(ActiveBasket basket)
    {
        var entries = basket.PricingBasis.Entries;
        if (entries.Count == 0) return [];

        var latest = new Dictionary<string, decimal>(
            capacity: entries.Count,
            comparer: StringComparer.OrdinalIgnoreCase);
        var prev = new Dictionary<string, decimal>(
            capacity: entries.Count,
            comparer: StringComparer.OrdinalIgnoreCase);

        foreach (var e in entries)
        {
            var s = _quotes.Get(e.Symbol);
            if (s is null) continue;
            if (s.Price > 0m) latest[e.Symbol] = s.Price;
            if (s.PreviousClose is > 0m) prev[e.Symbol] = s.PreviousClose.Value;
        }

        var rawValue = BasketRawValueCalculator.Compute(entries, latest);
        if (rawValue <= 0m) return [];

        var names = new Dictionary<string, string>(
            capacity: basket.Constituents.Count,
            comparer: StringComparer.OrdinalIgnoreCase);
        foreach (var c in basket.Constituents)
            names[c.Symbol] = c.SecurityName;

        var results = MoversCalculator.Compute(
            entries, latest, prev, rawValue, names, _options.MoversTopN);

        return results.Select(r => new MoverDto
        {
            Symbol = r.Symbol,
            Name = r.Name,
            ChangePct = r.ChangePct,
            Impact = r.Impact,
            Direction = r.Direction,
        }).ToList();
    }
}
