using Microsoft.Extensions.Options;
using Hqqq.Api.Configuration;
using Hqqq.Api.Modules.Basket.Contracts;
using Hqqq.Api.Modules.MarketData.Contracts;
using Hqqq.Api.Modules.Pricing.Contracts;
using Hqqq.Api.Modules.System.Services;

namespace Hqqq.Api.Modules.Pricing.Services;

/// <summary>
/// Core pricing engine: bootstrap calibration, synthetic NAV computation,
/// continuity-preserving basket activation, chart series, and movers.
/// Thread safety: <see cref="_currentBasis"/> and <see cref="_scaleState"/>
/// are swapped atomically via volatile references; the series ring buffer
/// is protected by <see cref="_seriesLock"/>.
/// </summary>
public sealed class PricingEngine
{
    private readonly IBasketSnapshotProvider _basketProvider;
    private readonly ILatestPriceStore _priceStore;
    private readonly IMarketDataIngestionService _marketData;
    private readonly IScaleStateStore _stateStore;
    private readonly BasketPricingBasisBuilder _basisBuilder;
    private readonly MetricsService _metrics;
    private readonly PricingOptions _options;
    private readonly TimeZoneInfo _marketTz;
    private readonly ILogger<PricingEngine> _logger;

    private volatile PricingBasis? _currentBasis;
    private volatile ScaleState _scaleState = ScaleState.Uninitialized;
    private readonly SemaphoreSlim _calibrationLock = new(1, 1);

    private readonly SeriesPoint?[] _seriesBuffer;
    private int _seriesHead;
    private int _seriesCount;
    private readonly object _seriesLock = new();

    private DateOnly _lastActivationDate;
    private string? _pendingBlockedReason;

    public bool IsInitialized => _scaleState.IsInitialized;
    public ScaleState CurrentScaleState => _scaleState;
    public PricingBasis? CurrentBasis => _currentBasis;
    public string? PendingBlockedReason => _pendingBlockedReason;
    public TimeZoneInfo MarketTimeZone => _marketTz;

    public PricingEngine(
        IBasketSnapshotProvider basketProvider,
        ILatestPriceStore priceStore,
        IMarketDataIngestionService marketData,
        IScaleStateStore stateStore,
        BasketPricingBasisBuilder basisBuilder,
        MetricsService metrics,
        IOptions<PricingOptions> options,
        ILogger<PricingEngine> logger)
    {
        _basketProvider = basketProvider;
        _priceStore = priceStore;
        _marketData = marketData;
        _stateStore = stateStore;
        _basisBuilder = basisBuilder;
        _metrics = metrics;
        _options = options.Value;
        _logger = logger;
        _seriesBuffer = new SeriesPoint?[_options.SeriesCapacity];
        _marketTz = ResolveTimeZone(_options.MarketTimeZone);
    }

    // ── Initialization ──────────────────────────────────────────

    /// <summary>
    /// Attempts to restore pricing state from the persisted file.
    /// Falls through to uninitialized if the persisted basket fingerprint
    /// doesn't match the current active basket.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct)
    {
        var persisted = await _stateStore.LoadAsync(ct);
        if (!persisted.IsInitialized || persisted.BasisEntries.Count == 0)
        {
            _logger.LogInformation("No usable persisted pricing state — will bootstrap");
            return;
        }

        var basketState = _basketProvider.GetState();
        if (basketState.ActiveFingerprint != persisted.BasketFingerprint)
        {
            _logger.LogWarning(
                "Persisted basket fp {Persisted} != active fp {Active} — will re-bootstrap",
                persisted.BasketFingerprint, basketState.ActiveFingerprint);
            return;
        }

        var basis = new PricingBasis
        {
            BasketFingerprint = persisted.BasketFingerprint,
            PricingBasisFingerprint = persisted.PricingBasisFingerprint,
            CreatedAtUtc = persisted.ActivatedAtUtc,
            Entries = persisted.BasisEntries,
            InferredTotalNotional = persisted.InferredTotalNotional,
            OfficialSharesCount = persisted.BasisEntries.Count(e => e.SharesOrigin == "official"),
            DerivedSharesCount = persisted.BasisEntries.Count(e => e.SharesOrigin == "derived"),
        };

        _currentBasis = basis;
        _scaleState = persisted;
        _logger.LogInformation(
            "Pricing state restored: basket {Fp}, {Count} entries, scale {Scale:E4}",
            persisted.BasketFingerprint[..Math.Min(8, persisted.BasketFingerprint.Length)],
            basis.Entries.Count,
            persisted.ScaleFactor);
    }

    // ── Bootstrap ───────────────────────────────────────────────

    public async Task<bool> TryBootstrapAsync(CancellationToken ct)
    {
        if (!await _calibrationLock.WaitAsync(0, ct)) return false;
        try
        {
            if (_scaleState.IsInitialized) return true;

            var basketState = _basketProvider.GetState();
            if (basketState.Active is null) return false;

            var qqq = _priceStore.Get("QQQ");
            if (qqq is null || qqq.IsStale || qqq.Price <= 0) return false;

            var health = _priceStore.GetHealthSnapshot();
            if (health.ActiveCoveragePct < 50) return false;

            var prices = BuildPriceMap(
                basketState.Active.Constituents.Select(c => c.Symbol));
            var basis = _basisBuilder.Build(basketState.Active, prices);
            if (basis.Entries.Count == 0) return false;

            var rawValue = ComputeRawValue(basis.Entries, prices);
            if (rawValue <= 0) return false;

            var scaleFactor = qqq.Price / rawValue;

            var newState = new ScaleState
            {
                ScaleFactor = scaleFactor,
                BasketFingerprint = basketState.ActiveFingerprint ?? "",
                PricingBasisFingerprint = basis.PricingBasisFingerprint,
                ActivatedAtUtc = DateTimeOffset.UtcNow,
                ComputedAtUtc = DateTimeOffset.UtcNow,
                InferredTotalNotional = basis.InferredTotalNotional,
                BasisEntries = basis.Entries,
            };

            await _stateStore.SaveAsync(newState, ct);

            _currentBasis = basis;
            _scaleState = newState;

            _logger.LogInformation(
                "Bootstrap complete: NAV≈{Nav:F2}, scale={Scale:E6}, {Count} entries",
                qqq.Price, scaleFactor, basis.Entries.Count);
            return true;
        }
        finally { _calibrationLock.Release(); }
    }

    // ── Continuity-preserving basket activation ─────────────────

    public async Task<bool> TryActivatePendingAsync(CancellationToken ct)
    {
        if (!_scaleState.IsInitialized || _currentBasis is null) return false;

        var basketState = _basketProvider.GetState();
        if (basketState.Pending is null)
        {
            _pendingBlockedReason = null;
            return false;
        }

        if (!IsWithinMarketHours())
        {
            _pendingBlockedReason = "Outside market hours";
            return false;
        }

        var today = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _marketTz));
        if (_lastActivationDate == today) { _pendingBlockedReason = null; return false; }

        var health = _priceStore.GetHealthSnapshot();
        if (!health.IsPendingBasketReady)
        {
            _pendingBlockedReason =
                $"Insufficient pending coverage ({health.PendingCoveragePct:F1}%)";
            return false;
        }

        if (!await _calibrationLock.WaitAsync(0, ct)) return false;
        try
        {
            var allSymbols = basketState.Active!.Constituents
                .Concat(basketState.Pending.Constituents)
                .Select(c => c.Symbol)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            var prices = BuildPriceMap(allSymbols);

            var oldRawValue = ComputeRawValue(_currentBasis.Entries, prices);
            var oldNav = _scaleState.ScaleFactor * oldRawValue;
            if (oldNav <= 0) { _pendingBlockedReason = "Cannot compute transition NAV"; return false; }

            var newBasis = _basisBuilder.Build(basketState.Pending, prices);
            if (newBasis.Entries.Count == 0) { _pendingBlockedReason = "Empty new basis"; return false; }

            var newRawValue = ComputeRawValue(newBasis.Entries, prices);
            if (newRawValue <= 0) { _pendingBlockedReason = "Zero new raw value"; return false; }

            var newScaleFactor = oldNav / newRawValue;

            // Measure NAV discontinuity before continuity-preserving recalibration
            var preRecalibrationNav = _scaleState.ScaleFactor * newRawValue;
            var jumpBps = (double)Math.Abs((preRecalibrationNav - oldNav) / oldNav * 10000m);
            _metrics.RecordActivationJump(jumpBps);

            _basketProvider.ActivatePendingIfReady();

            var afterState = _basketProvider.GetState();
            if (afterState.ActiveFingerprint == basketState.ActiveFingerprint)
            {
                _logger.LogWarning("Basket provider did not activate pending basket");
                return false;
            }

            var newScaleState = new ScaleState
            {
                ScaleFactor = newScaleFactor,
                BasketFingerprint = afterState.ActiveFingerprint ?? "",
                PricingBasisFingerprint = newBasis.PricingBasisFingerprint,
                ActivatedAtUtc = DateTimeOffset.UtcNow,
                ComputedAtUtc = DateTimeOffset.UtcNow,
                InferredTotalNotional = newBasis.InferredTotalNotional,
                BasisEntries = newBasis.Entries,
            };

            await _stateStore.SaveAsync(newScaleState, ct);

            _currentBasis = newBasis;
            _scaleState = newScaleState;
            _lastActivationDate = today;
            _pendingBlockedReason = null;

            _metrics.IncrementBasketActivations();
            _logger.LogInformation(
                "Pending basket activated: old NAV={OldNav:F2}, new scale={Scale:E6}, jump={Jump:F2}bps, continuity preserved",
                oldNav, newScaleFactor, jumpBps);
            return true;
        }
        finally { _calibrationLock.Release(); }
    }

    // ── Quote computation ───────────────────────────────────────

    public QuoteSnapshot? ComputeQuote()
    {
        var basis = _currentBasis;
        var state = _scaleState;
        if (basis is null || !state.IsInitialized) return null;

        var symbols = basis.Entries.Select(e => e.Symbol);
        var prices = BuildPriceMap(symbols);
        var rawValue = ComputeRawValue(basis.Entries, prices);
        var nav = state.ScaleFactor * rawValue;

        var prevClosePrices = BuildPreviousClosePriceMap(symbols);
        var prevCloseRawValue = ComputeRawValue(basis.Entries, prevClosePrices);
        var prevCloseNav = state.ScaleFactor * prevCloseRawValue;
        var navChangePct = prevCloseNav > 0
            ? (nav - prevCloseNav) / prevCloseNav * 100m : 0m;

        var qqq = _priceStore.Get("QQQ");
        var qqqPrice = qqq?.Price ?? 0m;
        var premiumDiscountPct = nav > 0
            ? (qqqPrice - nav) / nav * 100m : 0m;

        var basketValueB = rawValue / 1_000_000_000m;

        var feedHealth = _priceStore.GetHealthSnapshot();

        return new QuoteSnapshot
        {
            Nav = Math.Round(nav, 4),
            NavChangePct = Math.Round(navChangePct, 4),
            MarketPrice = Math.Round(qqqPrice, 2),
            PremiumDiscountPct = Math.Round(premiumDiscountPct, 4),
            Qqq = Math.Round(qqqPrice, 2),
            BasketValueB = Math.Round(basketValueB, 4),
            AsOf = DateTimeOffset.UtcNow,
            Series = GetSeries(),
            Movers = ComputeMovers(basis, prices, rawValue),
            Freshness = BuildFreshness(feedHealth),
            Feeds = BuildFeeds(feedHealth),
        };
    }

    // ── Constituents ────────────────────────────────────────────

    public ConstituentSnapshot? ComputeConstituents()
    {
        var basis = _currentBasis;
        var state = _scaleState;
        if (basis is null || !state.IsInitialized) return null;

        var basketState = _basketProvider.GetState();
        if (basketState.Active is null) return null;

        var constituentMap = basketState.Active.Constituents
            .ToDictionary(c => c.Symbol, StringComparer.OrdinalIgnoreCase);

        var rows = new List<ConstituentRow>();
        foreach (var entry in basis.Entries)
        {
            var ps = _priceStore.Get(entry.Symbol);
            constituentMap.TryGetValue(entry.Symbol, out var c);

            decimal? changePct = null;
            if (ps?.PreviousClose is > 0 && ps.Price > 0)
                changePct = Math.Round(
                    (ps.Price - ps.PreviousClose.Value) / ps.PreviousClose.Value * 100m, 2);

            rows.Add(new ConstituentRow
            {
                Symbol = entry.Symbol,
                Name = c?.SecurityName ?? entry.Symbol,
                Sector = c?.Sector ?? "Unknown",
                Weight = Math.Round((entry.TargetWeight ?? 0m) * 100m, 4),
                Shares = entry.Shares,
                Price = ps?.Price,
                ChangePct = changePct,
                MarketValue = ps is not null ? Math.Round(ps.Price * entry.Shares, 2) : null,
                SharesOrigin = entry.SharesOrigin,
                IsStale = ps?.IsStale ?? true,
            });
        }

        rows = rows.OrderByDescending(r => r.Weight).ToList();

        var concentration = new ConcentrationMetrics
        {
            Top5Pct = Math.Round(rows.Take(5).Sum(r => r.Weight), 2),
            Top10Pct = Math.Round(rows.Take(10).Sum(r => r.Weight), 2),
            Top20Pct = Math.Round(rows.Take(20).Sum(r => r.Weight), 2),
            SectorCount = rows.Select(r => r.Sector)
                .Where(s => s != "Unknown").Distinct().Count(),
            HerfindahlIndex = Math.Round(rows.Sum(r => r.Weight * r.Weight), 2),
        };

        var quality = new DataQualityMetrics
        {
            TotalSymbols = rows.Count,
            OfficialSharesCount = basis.OfficialSharesCount,
            DerivedSharesCount = basis.DerivedSharesCount,
            PricedCount = rows.Count(r => r.Price.HasValue),
            StaleCount = rows.Count(r => r.IsStale),
            PriceCoveragePct = rows.Count > 0
                ? Math.Round((decimal)rows.Count(r => r.Price.HasValue) / rows.Count * 100m, 2)
                : 0m,
            BasketMode = basketState.Active.QualityReport?.BasketMode ?? "unknown",
        };

        var source = new BasketSourceMetadata
        {
            AnchorSource = basketState.Active.QualityReport?.AnchorSource ?? "unknown",
            TailSource = basketState.Active.QualityReport?.TailSource ?? "unknown",
            BasketMode = basketState.Active.QualityReport?.BasketMode ?? "unknown",
            IsDegraded = basketState.Active.Source.IsDegraded,
            AsOfDate = basketState.Active.AsOfDate,
            Fingerprint = basketState.Active.Fingerprint,
        };

        return new ConstituentSnapshot
        {
            Holdings = rows,
            Concentration = concentration,
            Quality = quality,
            Source = source,
            AsOf = DateTimeOffset.UtcNow,
        };
    }

    // ── Observability helpers ─────────────────────────────────

    /// <summary>
    /// Fraction (0–1) of total basket weight covered by symbols that currently
    /// have a non-zero live price in the store.
    /// </summary>
    public double GetPricedWeightCoverage()
    {
        var basis = _currentBasis;
        if (basis is null || basis.Entries.Count == 0) return 0;

        decimal pricedWeight = 0m, totalWeight = 0m;
        foreach (var e in basis.Entries)
        {
            var w = e.TargetWeight ?? 0m;
            totalWeight += w;
            var ps = _priceStore.Get(e.Symbol);
            if (ps is not null && ps.Price > 0) pricedWeight += w;
        }
        return totalWeight > 0 ? (double)(pricedWeight / totalWeight) : 0;
    }

    // ── Series ring buffer ──────────────────────────────────────

    public void RecordSeriesPoint(QuoteSnapshot quote)
    {
        var point = new SeriesPoint
        {
            Time = quote.AsOf,
            Nav = quote.Nav,
            Market = quote.MarketPrice,
        };

        lock (_seriesLock)
        {
            _seriesBuffer[_seriesHead] = point;
            _seriesHead = (_seriesHead + 1) % _seriesBuffer.Length;
            if (_seriesCount < _seriesBuffer.Length) _seriesCount++;
        }
    }

    public IReadOnlyList<SeriesPoint> GetSeries()
    {
        lock (_seriesLock)
        {
            var result = new List<SeriesPoint>(_seriesCount);
            for (int i = 0; i < _seriesCount; i++)
            {
                var idx = (_seriesHead - _seriesCount + i + _seriesBuffer.Length)
                    % _seriesBuffer.Length;
                if (_seriesBuffer[idx] is { } point)
                    result.Add(point);
            }
            return result;
        }
    }

    public void ClearSeries()
    {
        lock (_seriesLock)
        {
            Array.Clear(_seriesBuffer);
            _seriesHead = 0;
            _seriesCount = 0;
        }
    }

    public void LoadSeries(IReadOnlyList<SeriesPoint> points)
    {
        lock (_seriesLock)
        {
            Array.Clear(_seriesBuffer);
            _seriesHead = 0;
            _seriesCount = 0;

            var count = Math.Min(points.Count, _seriesBuffer.Length);
            var start = points.Count > _seriesBuffer.Length
                ? points.Count - _seriesBuffer.Length
                : 0;

            for (int i = 0; i < count; i++)
            {
                _seriesBuffer[i] = points[start + i];
            }
            _seriesHead = count % _seriesBuffer.Length;
            _seriesCount = count;
        }
    }

    // ── Movers ──────────────────────────────────────────────────

    private IReadOnlyList<Mover> ComputeMovers(
        PricingBasis basis,
        IReadOnlyDictionary<string, decimal> prices,
        decimal rawValue)
    {
        if (rawValue <= 0) return [];

        var basketState = _basketProvider.GetState();
        var names = basketState.Active?.Constituents
            .ToDictionary(c => c.Symbol, c => c.SecurityName, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>();

        var candidates = new List<(string Sym, string Name, decimal Chg, decimal Imp)>();

        foreach (var entry in basis.Entries)
        {
            var ps = _priceStore.Get(entry.Symbol);
            if (ps?.PreviousClose is not > 0 || ps.Price <= 0) continue;

            var chg = (ps.Price - ps.PreviousClose.Value) / ps.PreviousClose.Value * 100m;
            if (prices.TryGetValue(entry.Symbol, out var price))
            {
                var weight = price * entry.Shares / rawValue;
                var impact = weight * chg * 100m;
                names.TryGetValue(entry.Symbol, out var name);
                candidates.Add((entry.Symbol, name ?? entry.Symbol, chg, impact));
            }
        }

        return candidates
            .OrderByDescending(m => Math.Abs(m.Imp))
            .Take(5)
            .Select(m => new Mover
            {
                Symbol = m.Sym,
                Name = m.Name,
                ChangePct = Math.Round(m.Chg, 2),
                Impact = Math.Round(m.Imp, 2),
                Direction = m.Chg >= 0 ? "up" : "down",
            })
            .ToList();
    }

    // ── Helpers ──────────────────────────────────────────────────

    private IReadOnlyDictionary<string, decimal> BuildPriceMap(IEnumerable<string> symbols)
    {
        var map = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in symbols)
        {
            var ps = _priceStore.Get(s);
            if (ps is not null && ps.Price > 0) map[s] = ps.Price;
        }
        return map;
    }

    private IReadOnlyDictionary<string, decimal> BuildPreviousClosePriceMap(
        IEnumerable<string> symbols)
    {
        var map = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in symbols)
        {
            var ps = _priceStore.Get(s);
            if (ps?.PreviousClose is > 0)
                map[s] = ps.PreviousClose.Value;
            else if (ps is not null && ps.Price > 0)
                map[s] = ps.Price;
        }
        return map;
    }

    private static decimal ComputeRawValue(
        IReadOnlyList<PricingBasisEntry> entries,
        IReadOnlyDictionary<string, decimal> prices)
    {
        decimal total = 0m;
        foreach (var e in entries)
            if (prices.TryGetValue(e.Symbol, out var p))
                total += p * e.Shares;
        return total;
    }

    private FreshnessInfo BuildFreshness(FeedHealthSnapshot health)
    {
        var fresh = health.SymbolsTracked - health.StaleSymbolCount;
        return new FreshnessInfo
        {
            SymbolsTotal = health.SymbolsTracked,
            SymbolsFresh = fresh,
            SymbolsStale = health.StaleSymbolCount,
            FreshPct = health.SymbolsTracked > 0
                ? Math.Round((decimal)fresh / health.SymbolsTracked * 100m, 1) : 0m,
            LastTickUtc = health.LastUpstreamActivityUtc,
            AvgTickIntervalMs = health.AverageTickIntervalMs,
        };
    }

    private FeedInfo BuildFeeds(FeedHealthSnapshot health)
    {
        var bs = _basketProvider.GetState();
        var bState = bs.Active is not null
            ? (bs.Pending is not null ? "active+pending" : "active")
            : "unavailable";

        var pendingBlocked = bs.Pending is not null && !health.IsPendingBasketReady;

        return new FeedInfo
        {
            WebSocketConnected = _marketData.IsWebSocketConnected,
            FallbackActive = _marketData.IsFallbackActive,
            PricingActive = _scaleState.IsInitialized,
            BasketState = bState,
            PendingActivationBlocked = pendingBlocked,
            PendingBlockedReason = _pendingBlockedReason,
        };
    }

    public bool IsWithinMarketHours()
    {
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _marketTz);
        var time = TimeOnly.FromDateTime(now);
        return time >= new TimeOnly(9, 30) && time < new TimeOnly(16, 0);
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) when (id == "America/New_York")
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
    }
}
