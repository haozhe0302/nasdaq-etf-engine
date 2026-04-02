using Hqqq.Api.Configuration;
using Hqqq.Api.Modules.Basket.Contracts;
using Microsoft.Extensions.Options;

namespace Hqqq.Api.Modules.Basket.Services;

/// <summary>
/// Orchestrates the four-source basket pipeline:
///   1. Fetch all raw sources (Stock Analysis, Schwab, Alpha Vantage, Nasdaq).
///   2. Select anchor (Stock Analysis preferred, Schwab fallback).
///   3. Select tail (Alpha Vantage preferred, Nasdaq fallback).
///   4. Merge into a hybrid candidate basket.
///   5. Assign as active (bootstrap) or pending (normal).
/// </summary>
public sealed class BasketSnapshotProvider : IBasketSnapshotProvider
{
    private readonly StockAnalysisAdapter _stockAnalysis;
    private readonly SchwabHoldingsAdapter _schwab;
    private readonly AlphaVantageAdapter _alphaVantage;
    private readonly NasdaqHoldingsAdapter _nasdaq;
    private readonly BasketCacheService _cache;
    private readonly RawSourceCacheService _rawCache;
    private readonly MarketHoursHelper _market;
    private readonly ILogger<BasketSnapshotProvider> _logger;

    private readonly object _stateLock = new();
    private readonly BasketState _state = new();
    private List<SourceFetchOutcome> _lastFetchOutcomes = [];

    public BasketSnapshotProvider(
        StockAnalysisAdapter stockAnalysis,
        SchwabHoldingsAdapter schwab,
        AlphaVantageAdapter alphaVantage,
        NasdaqHoldingsAdapter nasdaq,
        BasketCacheService cache,
        RawSourceCacheService rawCache,
        IOptions<PricingOptions> pricingOpts,
        ILogger<BasketSnapshotProvider> logger)
    {
        _stockAnalysis = stockAnalysis;
        _schwab = schwab;
        _alphaVantage = alphaVantage;
        _nasdaq = nasdaq;
        _cache = cache;
        _rawCache = rawCache;
        _logger = logger;
        _market = new MarketHoursHelper(
            TimeZoneInfo.FindSystemTimeZoneById(pricingOpts.Value.MarketTimeZone));
    }

    public Task<BasketSnapshot?> GetLatestAsync(CancellationToken ct = default)
    {
        lock (_stateLock) { return Task.FromResult(_state.Active); }
    }

    public BasketState GetState()
    {
        lock (_stateLock)
        {
            return new BasketState
            {
                Active = _state.Active,
                Pending = _state.Pending,
                PendingEffectiveAtUtc = _state.PendingEffectiveAtUtc,
                LastError = _state.LastError,
            };
        }
    }

    public List<SourceFetchOutcome> GetLastFetchOutcomes()
    {
        lock (_stateLock) { return [.._lastFetchOutcomes]; }
    }

    public void ActivatePendingIfReady()
    {
        lock (_stateLock)
        {
            if (_state.Pending is null) return;
            _state.Active = _state.Pending;
            _state.Pending = null;
            _state.PendingEffectiveAtUtc = null;
            _logger.LogInformation("Pending basket activated: {Count} constituents",
                _state.Active.Constituents.Count);
        }
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var outcomes = new List<SourceFetchOutcome>();

        // ── Fetch all four sources ──
        var saResult = await FetchSource("stockanalysis",
            () => _stockAnalysis.FetchAsync(ct), outcomes, ct);
        var schwabResult = await FetchSource("schwab",
            () => _schwab.FetchAsync(ct), outcomes, ct);
        var alphaResult = await FetchSource("alphavantage",
            () => _alphaVantage.FetchAsync(ct), outcomes, ct);
        var nasdaqResult = await FetchSource("nasdaq",
            () => _nasdaq.FetchAsync(ct), outcomes, ct);

        lock (_stateLock) { _lastFetchOutcomes = outcomes; }

        // ── Build anchor block ──
        var anchor = BuildAnchor(saResult, schwabResult);
        if (anchor is null)
        {
            // Try cache fallback
            var cached = await TryCacheAsync(ct);
            if (cached is not null) { ApplySnapshot(cached); return; }

            // Try Nasdaq-only degraded
            if (nasdaqResult is not null)
            {
                var degraded = BuildNasdaqDegraded(nasdaqResult);
                if (degraded is not null) { ApplySnapshot(degraded); return; }
            }

            lock (_stateLock) { _state.LastError = "All basket sources failed"; }
            _logger.LogError("Basket refresh failed: no anchor source available");
            return;
        }

        // ── Build tail block ──
        var tail = BuildTail(alphaResult, nasdaqResult, anchor);

        // ── Merge ──
        var mergeResult = MergedBasketBuilder.Build(anchor, tail);

        var snapshot = new BasketSnapshot
        {
            AsOfDate = mergeResult.AsOfDate,
            Constituents = mergeResult.Constituents,
            FetchedAtUtc = DateTimeOffset.UtcNow,
            QualityReport = mergeResult.QualityReport,
            Source = new BasketSourceInfo
            {
                SourceName = $"{anchor.SourceName}+{tail.SourceName}",
                SourceType = tail.IsProxy ? "degraded-fallback" : "primary",
                IsDegraded = tail.IsProxy,
                SourceAsOfDate = mergeResult.AsOfDate,
                FetchedAtUtc = DateTimeOffset.UtcNow,
                CacheWrittenAtUtc = DateTimeOffset.MinValue,
                OfficialWeightsAvailable = true,
                OfficialSharesAvailable = anchor.Constituents.Any(c => c.SharesHeld > 0),
            },
        };

        await _cache.SaveAsync(snapshot, ct);
        ApplySnapshot(snapshot);
    }

    #region Anchor construction

    private MergedBasketBuilder.AnchorBlock? BuildAnchor(
        StockAnalysisAdapter.StockAnalysisResult? sa,
        SchwabHoldingsAdapter.SchwabFetchResult? schwab)
    {
        if (sa is { Holdings.Count: > 0 })
        {
            var constituents = sa.Holdings.Select(h => new BasketConstituent
            {
                Symbol = h.Symbol,
                SecurityName = h.Name,
                Exchange = "NASDAQ",
                Currency = "USD",
                Weight = h.WeightPct / 100m,
                SharesHeld = h.Shares,
                AsOfDate = sa.AsOfDate,
                WeightSource = "stockanalysis",
                SharesSource = h.Shares > 0 ? "stockanalysis" : "unavailable",
                NameSource = "stockanalysis",
                SectorSource = "unknown",
            }).ToList();

            _logger.LogInformation("Anchor: Stock Analysis ({Count} holdings)", constituents.Count);
            return new(constituents, "stockanalysis", sa.AsOfDate);
        }

        if (schwab is { Constituents.Count: > 0 })
        {
            var constituents = schwab.Constituents.Select(c => c with
            {
                WeightSource = "schwab",
                SharesSource = c.SharesHeld > 0 ? "schwab" : "unavailable",
                NameSource = "schwab",
            }).ToList();

            _logger.LogInformation("Anchor fallback: Schwab ({Count} holdings)", constituents.Count);
            return new(constituents, "schwab", schwab.AsOfDate);
        }

        _logger.LogWarning("No anchor source available");
        return null;
    }

    #endregion

    #region Tail construction

    private MergedBasketBuilder.TailBlock BuildTail(
        AlphaVantageAdapter.AlphaVantageResult? alpha,
        IReadOnlyList<BasketConstituent>? nasdaq,
        MergedBasketBuilder.AnchorBlock anchor)
    {
        if (alpha is { Holdings.Count: > 0 })
        {
            var entries = alpha.Holdings.Select(h =>
                new MergedBasketBuilder.TailEntry(
                    h.Symbol, h.Description, h.Weight, "Unknown"))
                .ToList();

            _logger.LogInformation("Tail: Alpha Vantage ({Count} entries)", entries.Count);
            return new(entries, "alphavantage", IsProxy: false);
        }

        if (nasdaq is { Count: > 0 })
        {
            var entries = nasdaq.Select(c =>
                new MergedBasketBuilder.TailEntry(
                    c.Symbol, c.SecurityName, c.Weight ?? 0m, c.Sector))
                .ToList();

            _logger.LogWarning("Tail fallback: Nasdaq proxy ({Count} entries)", entries.Count);
            return new(entries, "nasdaq-proxy", IsProxy: true);
        }

        _logger.LogWarning("No tail source available — anchor-only basket");
        return new([], "none", IsProxy: true);
    }

    #endregion

    #region Fallbacks

    private async Task<BasketSnapshot?> TryCacheAsync(CancellationToken ct)
    {
        try
        {
            var cached = await _cache.LoadAsync(ct);
            if (cached is { Constituents.Count: > 0 })
            {
                _logger.LogInformation("Cache hit: {Count} constituents", cached.Constituents.Count);
                return cached;
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Cache load failed"); }
        return null;
    }

    private BasketSnapshot? BuildNasdaqDegraded(IReadOnlyList<BasketConstituent> nasdaq)
    {
        var asOf = nasdaq[0].AsOfDate;
        var degraded = nasdaq.Select(c => c with
        {
            WeightSource = "nasdaq-proxy",
            SharesSource = "unavailable",
            NameSource = "nasdaq",
        }).ToList();

        return new BasketSnapshot
        {
            AsOfDate = asOf,
            Constituents = degraded,
            FetchedAtUtc = DateTimeOffset.UtcNow,
            Source = new BasketSourceInfo
            {
                SourceName = "nasdaq-degraded",
                SourceType = "degraded-fallback",
                IsDegraded = true,
                SourceAsOfDate = asOf,
                FetchedAtUtc = DateTimeOffset.UtcNow,
                CacheWrittenAtUtc = DateTimeOffset.MinValue,
                OfficialWeightsAvailable = false,
                OfficialSharesAvailable = false,
            },
            QualityReport = new BasketQualityReport
            {
                OfficialWeightCoveragePct = 0,
                OfficialSharesCoveragePct = 0,
                ProxyTailCoveragePct = 100,
                FilteredRowCount = 0,
                DroppedDirtySymbolCount = 0,
                TotalSymbolCount = degraded.Count,
                IsDegraded = true,
                TotalWeightPct = Math.Round(degraded.Sum(c => c.Weight ?? 0m) * 100m, 2),
                BasketMode = "degraded",
                AnchorSource = "none",
                TailSource = "nasdaq-proxy",
                AnchorCount = 0,
                TailCount = degraded.Count,
            },
        };
    }

    #endregion

    #region Active/pending

    private void ApplySnapshot(BasketSnapshot snapshot)
    {
        lock (_stateLock)
        {
            _state.LastError = null;

            if (_state.Active is null)
            {
                _state.Active = snapshot;
                _state.Pending = null;
                _state.PendingEffectiveAtUtc = null;
                _logger.LogInformation("Bootstrap: basket set as active ({Count} constituents)",
                    snapshot.Constituents.Count);
            }
            else
            {
                _state.Pending = snapshot;
                _state.PendingEffectiveAtUtc = _market.NextMarketOpenUtc();
                _logger.LogInformation("Basket stored as pending for {EffectiveAt}",
                    _state.PendingEffectiveAtUtc);
            }
        }
    }

    #endregion

    #region Source fetch helper

    private async Task<T?> FetchSource<T>(
        string name, Func<Task<T>> fetcher,
        List<SourceFetchOutcome> outcomes, CancellationToken ct)
        where T : class
    {
        try
        {
            var result = await fetcher();
            await _rawCache.SaveAsync(name, result, ct);
            outcomes.Add(new SourceFetchOutcome
            {
                SourceName = name,
                Success = true,
                FetchedAtUtc = DateTimeOffset.UtcNow,
                RowCount = GetRowCount(result),
            });
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fetch failed for {Source}", name);
            outcomes.Add(new SourceFetchOutcome
            {
                SourceName = name,
                Success = false,
                FetchedAtUtc = DateTimeOffset.UtcNow,
                Error = ex.Message,
            });
            return null;
        }
    }

    private static int GetRowCount(object result) => result switch
    {
        StockAnalysisAdapter.StockAnalysisResult sa => sa.Holdings.Count,
        SchwabHoldingsAdapter.SchwabFetchResult s => s.Constituents.Count,
        AlphaVantageAdapter.AlphaVantageResult a => a.FilteredCount,
        IReadOnlyList<BasketConstituent> l => l.Count,
        _ => 0,
    };

    #endregion
}
