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
    private static readonly TimeOnly PreOpenResetStart = new(9, 25);
    private static readonly TimeOnly RegularOpen = new(9, 30);
    private static readonly TimeOnly RegularClose = new(16, 0);

    private readonly PerSymbolQuoteStore _quotes;
    private readonly BasketStateStore _baskets;
    private readonly EngineRuntimeState _runtime;
    private readonly ISystemClock _clock;
    private readonly QuoteEngineOptions _options;
    private readonly TimeZoneInfo _marketTimeZone;

    private DateTimeOffset _nextSeriesRecordAtUtc = DateTimeOffset.MinValue;
    private DateOnly? _lastPreOpenResetEtDay;

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
        _marketTimeZone = ResolveMarketTimeZone(options.MarketTimeZone);
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
        var marketLocal = TimeZoneInfo.ConvertTime(now.ToUniversalTime(), _marketTimeZone);
        var marketDate = DateOnly.FromDateTime(marketLocal.DateTime);
        var marketTime = TimeOnly.FromDateTime(marketLocal.DateTime);
        var isWeekday = marketLocal.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday;

        // Daily pre-open reset: clear intraday chart once per ET day in the
        // 09:25–09:30 window so the next session starts from an empty series.
        if (_options.EnablePreOpenSeriesReset
            && isWeekday
            && marketTime >= PreOpenResetStart
            && marketTime < RegularOpen
            && _lastPreOpenResetEtDay != marketDate)
        {
            _runtime.ClearSeries();
            _lastPreOpenResetEtDay = marketDate;
        }

        // Keep series strictly regular-session-only (09:30–16:00 ET).
        if (!isWeekday || marketTime < RegularOpen || marketTime >= RegularClose)
            return;

        if (now < _nextSeriesRecordAtUtc) return;

        _runtime.RecordSeriesPoint(new Hqqq.Contracts.Dtos.SeriesPointDto
        {
            Time = now,
            Nav = Math.Round(nav, 4),
            Market = Math.Round(marketPrice, 2),
        });

        _nextSeriesRecordAtUtc = now + _options.SeriesRecordInterval;
    }

    private static TimeZoneInfo ResolveMarketTimeZone(string? configuredId)
    {
        if (!string.IsNullOrWhiteSpace(configuredId))
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(configuredId); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        foreach (var id in new[] { "America/New_York", "Eastern Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        return TimeZoneInfo.Utc;
    }
}
