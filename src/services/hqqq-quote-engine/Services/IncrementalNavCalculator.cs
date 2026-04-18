using Hqqq.Domain.Services;
using Hqqq.QuoteEngine.Abstractions;
using Hqqq.QuoteEngine.Models;
using Hqqq.QuoteEngine.State;

namespace Hqqq.QuoteEngine.Services;

/// <summary>
/// Computes the next set of NAV-centric scalars from the current
/// <see cref="PerSymbolQuoteStore"/> + <see cref="BasketStateStore"/> and
/// writes them into <see cref="EngineRuntimeState"/>. Pure math delegates to
/// <see cref="BasketRawValueCalculator"/> and <see cref="PremiumDiscountCalculator"/>
/// in <c>Hqqq.Domain</c>; this class only sequences store reads and writes.
/// </summary>
public sealed class IncrementalNavCalculator
{
    private readonly PerSymbolQuoteStore _quotes;
    private readonly BasketStateStore _baskets;
    private readonly EngineRuntimeState _runtime;
    private readonly ISystemClock _clock;
    private readonly QuoteEngineOptions _options;

    private DateTimeOffset _nextSeriesRecordAtUtc = DateTimeOffset.MinValue;

    public IncrementalNavCalculator(
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

    /// <summary>
    /// Run one compute cycle. Returns false if the engine is not ready
    /// (no basket / no scale factor) or the basket has no priced entries.
    /// </summary>
    public bool TryRecompute()
    {
        var basket = _baskets.Current;
        if (basket is null || !basket.ScaleFactor.IsInitialized)
        {
            _runtime.SetReadiness(QuoteReadiness.Uninitialized);
            return false;
        }

        var basis = basket.PricingBasis;
        var now = _clock.UtcNow;

        var latestPrices = BuildPriceMap(basis);
        if (latestPrices.Count == 0)
        {
            _runtime.SetReadiness(
                QuoteReadiness.FrozenAllStale,
                "No priced constituents available");
            return false;
        }

        var rawValue = BasketRawValueCalculator.Compute(basis.Entries, latestPrices);
        var nav = basket.ScaleFactor.Value * rawValue;

        var anchor = _quotes.Get(_options.AnchorSymbol);
        var qqqPrice = anchor?.Price ?? 0m;

        var premiumDiscountPct = PremiumDiscountCalculator.Calculate(nav, qqqPrice);

        var navChangePct = ComputeNavChangePct(
            basket, basis.Entries, latestPrices, nav);

        var qqqChangePct = ComputeAnchorChangePct(anchor, basket.QqqPreviousClose);

        var basketValueB = rawValue / 1_000_000_000m;

        var lastTickUtc = FindMostRecentTick(basis);

        _runtime.UpdateScalars(
            nav: Math.Round(nav, 4),
            navChangePct: Math.Round(navChangePct, 4),
            marketPrice: Math.Round(qqqPrice, 2),
            premiumDiscountPct: Math.Round(premiumDiscountPct, 4),
            qqq: Math.Round(qqqPrice, 2),
            qqqChangePct: Math.Round(qqqChangePct, 4),
            basketValueB: Math.Round(basketValueB, 4),
            computedAtUtc: now,
            lastTickUtc: lastTickUtc);

        MaybeRecordSeriesPoint(now, nav, qqqPrice);

        // Freshness-driven readiness flip: mirror the legacy rule —
        // if every tracked symbol is stale, emit a frozen marker.
        var trackedSymbols = basis.Entries.Select(e => e.Symbol).ToList();
        var freshness = _quotes.BuildFreshnessSummary(trackedSymbols, _options.StaleAfter);
        var allStale = freshness.SymbolsTotal > 0
            && freshness.SymbolsStale >= freshness.SymbolsTotal;
        if (allStale)
            _runtime.SetReadiness(QuoteReadiness.FrozenAllStale, "All tracked symbols are stale");
        else
            _runtime.SetReadiness(QuoteReadiness.Live);

        return true;
    }

    private IReadOnlyDictionary<string, decimal> BuildPriceMap(
        Hqqq.Domain.Entities.PricingBasis basis)
    {
        var map = new Dictionary<string, decimal>(
            capacity: basis.Entries.Count,
            comparer: StringComparer.OrdinalIgnoreCase);
        foreach (var e in basis.Entries)
        {
            var state = _quotes.Get(e.Symbol);
            if (state is not null && state.Price > 0m)
                map[e.Symbol] = state.Price;
        }
        return map;
    }

    private decimal ComputeNavChangePct(
        ActiveBasket basket,
        IReadOnlyList<Hqqq.Domain.Entities.PricingBasisEntry> entries,
        IReadOnlyDictionary<string, decimal> latestPrices,
        decimal nav)
    {
        if (basket.NavPreviousClose is > 0m)
            return (nav - basket.NavPreviousClose.Value)
                / basket.NavPreviousClose.Value * 100m;

        var prevClosePrices = BuildPreviousClosePriceMap(entries, latestPrices);
        var prevCloseRaw = BasketRawValueCalculator.Compute(entries, prevClosePrices);
        var prevCloseNav = basket.ScaleFactor.Value * prevCloseRaw;

        return prevCloseNav > 0m
            ? (nav - prevCloseNav) / prevCloseNav * 100m
            : 0m;
    }

    private IReadOnlyDictionary<string, decimal> BuildPreviousClosePriceMap(
        IReadOnlyList<Hqqq.Domain.Entities.PricingBasisEntry> entries,
        IReadOnlyDictionary<string, decimal> latestPrices)
    {
        var map = new Dictionary<string, decimal>(
            capacity: entries.Count,
            comparer: StringComparer.OrdinalIgnoreCase);
        foreach (var e in entries)
        {
            var state = _quotes.Get(e.Symbol);
            if (state?.PreviousClose is > 0m)
                map[e.Symbol] = state.PreviousClose.Value;
            else if (latestPrices.TryGetValue(e.Symbol, out var p))
                map[e.Symbol] = p;
        }
        return map;
    }

    private static decimal ComputeAnchorChangePct(
        PerSymbolQuoteState? anchor,
        decimal? previousClose)
    {
        if (anchor is null || anchor.Price <= 0m) return 0m;

        var prev = previousClose ?? anchor.PreviousClose;
        if (prev is not > 0m) return 0m;

        return (anchor.Price - prev.Value) / prev.Value * 100m;
    }

    private DateTimeOffset? FindMostRecentTick(Hqqq.Domain.Entities.PricingBasis basis)
    {
        DateTimeOffset? latest = null;
        foreach (var e in basis.Entries)
        {
            var state = _quotes.Get(e.Symbol);
            if (state is null) continue;
            if (latest is null || state.ReceivedAtUtc > latest)
                latest = state.ReceivedAtUtc;
        }
        return latest;
    }

    private void MaybeRecordSeriesPoint(DateTimeOffset now, decimal nav, decimal marketPrice)
    {
        if (now < _nextSeriesRecordAtUtc) return;

        _runtime.RecordSeriesPoint(new Hqqq.Contracts.Dtos.SeriesPointDto
        {
            Time = now,
            Nav = Math.Round(nav, 4),
            Market = Math.Round(marketPrice, 2),
        });

        _nextSeriesRecordAtUtc = now + _options.SeriesRecordInterval;
    }
}
