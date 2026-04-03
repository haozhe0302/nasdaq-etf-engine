using Hqqq.Api.Configuration;
using Hqqq.Api.Modules.Basket.Contracts;
using Microsoft.Extensions.Options;

namespace Hqqq.Api.Modules.Basket.Services;

/// <summary>
/// Orchestrates the four-source basket pipeline with raw-cache fallback,
/// date-based anchor selection, universe-constrained tail, fingerprint
/// idempotency, and active/pending basket semantics.
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
                ActiveFingerprint = _state.Active?.Fingerprint,
                PendingFingerprint = _state.Pending?.Fingerprint,
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
            _logger.LogInformation("Pending basket activated: {Count} constituents, fingerprint {FP}",
                _state.Active.Constituents.Count, _state.Active.Fingerprint);
        }
    }

    /// <summary>Fetch all raw sources only — save to per-source raw caches.</summary>
    public async Task FetchRawSourcesAsync(CancellationToken ct = default)
    {
        var outcomes = new List<SourceFetchOutcome>();
        await FetchSource<StockAnalysisAdapter.StockAnalysisResult>(
            "stockanalysis", () => _stockAnalysis.FetchAsync(ct), outcomes, ct);
        await FetchSource<SchwabHoldingsAdapter.SchwabFetchResult>(
            "schwab", () => _schwab.FetchAsync(ct), outcomes, ct);
        await FetchSource<AlphaVantageAdapter.AlphaVantageResult>(
            "alphavantage", () => _alphaVantage.FetchAsync(ct), outcomes, ct);
        await FetchSource<NasdaqHoldingsAdapter.NasdaqFetchResult>(
            "nasdaq", () => _nasdaq.FetchAsync(ct), outcomes, ct);
        lock (_stateLock) { _lastFetchOutcomes = outcomes; }
        _logger.LogInformation("Raw source fetch complete: {Ok}/{Total} succeeded",
            outcomes.Count(o => o.Success), outcomes.Count);
    }

    /// <summary>Merge from whatever raw caches exist into a candidate basket.</summary>
    public async Task MergeAndApplyAsync(CancellationToken ct = default)
    {
        var outcomes = GetLastFetchOutcomes();

        var saResult = await _rawCache.LoadAsync<StockAnalysisAdapter.StockAnalysisResult>("stockanalysis", ct);
        var schwabResult = await _rawCache.LoadAsync<SchwabHoldingsAdapter.SchwabFetchResult>("schwab", ct);
        var alphaResult = await _rawCache.LoadAsync<AlphaVantageAdapter.AlphaVantageResult>("alphavantage", ct);
        var nasdaqResult = await _rawCache.LoadAsync<NasdaqHoldingsAdapter.NasdaqFetchResult>("nasdaq", ct);

        BuildAndApply(saResult, schwabResult, alphaResult, nasdaqResult,
            usedRawCache: true, outcomes, ct).GetAwaiter().GetResult();
    }

    /// <summary>Full refresh: fetch all sources live, then merge and apply.</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        var outcomes = new List<SourceFetchOutcome>();

        var saResult = await FetchSource<StockAnalysisAdapter.StockAnalysisResult>(
            "stockanalysis", () => _stockAnalysis.FetchAsync(ct), outcomes, ct);
        var schwabResult = await FetchSource<SchwabHoldingsAdapter.SchwabFetchResult>(
            "schwab", () => _schwab.FetchAsync(ct), outcomes, ct);
        var alphaResult = await FetchSource<AlphaVantageAdapter.AlphaVantageResult>(
            "alphavantage", () => _alphaVantage.FetchAsync(ct), outcomes, ct);
        var nasdaqResult = await FetchSource<NasdaqHoldingsAdapter.NasdaqFetchResult>(
            "nasdaq", () => _nasdaq.FetchAsync(ct), outcomes, ct);

        lock (_stateLock) { _lastFetchOutcomes = outcomes; }

        var anyUsedCache = outcomes.Any(o => o.Origin == "raw-cache");
        await BuildAndApply(saResult, schwabResult, alphaResult, nasdaqResult,
            anyUsedCache, outcomes, ct);
    }

    #region Core merge pipeline

    private async Task BuildAndApply(
        StockAnalysisAdapter.StockAnalysisResult? saResult,
        SchwabHoldingsAdapter.SchwabFetchResult? schwabResult,
        AlphaVantageAdapter.AlphaVantageResult? alphaResult,
        NasdaqHoldingsAdapter.NasdaqFetchResult? nasdaqResult,
        bool usedRawCache,
        List<SourceFetchOutcome> outcomes,
        CancellationToken ct)
    {
        var anchor = BuildAnchor(saResult, schwabResult);
        if (anchor is null)
        {
            var cached = await TryCacheAsync(ct);
            if (cached is not null) { ApplySnapshot(cached); return; }

            if (nasdaqResult is { Constituents.Count: > 0 })
            {
                var degraded = BuildNasdaqDegraded(nasdaqResult);
                ApplySnapshot(degraded);
                return;
            }

            lock (_stateLock) { _state.LastError = "All basket sources failed"; }
            _logger.LogError("Basket merge failed: no anchor source available");
            return;
        }

        var nasdaqUniverse = nasdaqResult?.Constituents
            .Select(c => c.Symbol)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var tail = BuildTail(alphaResult, nasdaqResult, nasdaqUniverse);
        var mergeResult = MergedBasketBuilder.Build(anchor, tail, nasdaqUniverse);

        var snapshot = new BasketSnapshot
        {
            AsOfDate = mergeResult.AsOfDate,
            Constituents = mergeResult.Constituents,
            FetchedAtUtc = DateTimeOffset.UtcNow,
            QualityReport = mergeResult.QualityReport,
            Fingerprint = mergeResult.Fingerprint,
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
                HasProxyTail = tail.Entries.Count > 0,
                UsedRawSourceCache = usedRawCache,
                BasketMode = mergeResult.QualityReport.BasketMode,
            },
        };

        await _cache.SaveAsync(snapshot, ct);
        ApplySnapshot(snapshot);
    }

    #endregion

    #region Anchor: date-based selection

    private MergedBasketBuilder.AnchorBlock? BuildAnchor(
        StockAnalysisAdapter.StockAnalysisResult? sa,
        SchwabHoldingsAdapter.SchwabFetchResult? schwab)
    {
        var saOk = sa is { Holdings.Count: > 0 };
        var schwabOk = schwab is { Constituents.Count: > 0 };

        if (saOk && schwabOk)
        {
            var saDate = sa!.AsOfDate;
            var schwabDate = schwab!.AsOfDate;

            if (saDate > schwabDate)
                return BuildSaAnchor(sa!);
            if (schwabDate > saDate)
                return BuildSchwabAnchor(schwab!);
            return BuildSaAnchor(sa!);
        }

        if (saOk) return BuildSaAnchor(sa!);
        if (schwabOk) return BuildSchwabAnchor(schwab!);

        _logger.LogWarning("No anchor source available");
        return null;
    }

    private MergedBasketBuilder.AnchorBlock BuildSaAnchor(StockAnalysisAdapter.StockAnalysisResult sa)
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

        _logger.LogInformation("Anchor: Stock Analysis ({Count} holdings, as-of {AsOf})",
            constituents.Count, sa.AsOfDate);
        return new(constituents, "stockanalysis", sa.AsOfDate);
    }

    private MergedBasketBuilder.AnchorBlock BuildSchwabAnchor(SchwabHoldingsAdapter.SchwabFetchResult schwab)
    {
        var constituents = schwab.Constituents.Select(c => c with
        {
            WeightSource = "schwab",
            SharesSource = c.SharesHeld > 0 ? "schwab" : "unavailable",
            NameSource = "schwab",
        }).ToList();

        _logger.LogInformation("Anchor: Schwab ({Count} holdings, as-of {AsOf})",
            constituents.Count, schwab.AsOfDate);
        return new(constituents, "schwab", schwab.AsOfDate);
    }

    #endregion

    #region Tail: universe-constrained

    private MergedBasketBuilder.TailBlock BuildTail(
        AlphaVantageAdapter.AlphaVantageResult? alpha,
        NasdaqHoldingsAdapter.NasdaqFetchResult? nasdaq,
        HashSet<string>? universeSymbols)
    {
        if (alpha is { Holdings.Count: > 0 })
        {
            var entries = alpha.Holdings.Select(h =>
                new MergedBasketBuilder.TailEntry(
                    h.Symbol, h.Description, h.Weight, "Unknown"))
                .ToList();

            _logger.LogInformation("Tail: Alpha Vantage ({Count} entries, universe guardrail {HasUniverse})",
                entries.Count, universeSymbols is { Count: > 0 });
            return new(entries, "alphavantage", IsProxy: false);
        }

        if (nasdaq is { Constituents.Count: > 0 })
        {
            var entries = nasdaq.Constituents.Select(c =>
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

    private BasketSnapshot BuildNasdaqDegraded(NasdaqHoldingsAdapter.NasdaqFetchResult nasdaq)
    {
        var degraded = nasdaq.Constituents.Select(c => c with
        {
            WeightSource = "nasdaq-proxy",
            SharesSource = "unavailable",
            NameSource = "nasdaq",
        }).ToList();

        var fp = MergedBasketBuilder.ComputeFingerprint(degraded, nasdaq.AsOfDate);

        return new BasketSnapshot
        {
            AsOfDate = nasdaq.AsOfDate,
            Constituents = degraded,
            FetchedAtUtc = DateTimeOffset.UtcNow,
            Fingerprint = fp,
            Source = new BasketSourceInfo
            {
                SourceName = "nasdaq-degraded",
                SourceType = "degraded-fallback",
                IsDegraded = true,
                SourceAsOfDate = nasdaq.AsOfDate,
                FetchedAtUtc = DateTimeOffset.UtcNow,
                CacheWrittenAtUtc = DateTimeOffset.MinValue,
                OfficialWeightsAvailable = false,
                OfficialSharesAvailable = false,
                HasProxyTail = true,
                BasketMode = "degraded",
            },
            QualityReport = new BasketQualityReport
            {
                OfficialWeightCoveragePct = 0,
                OfficialSharesByWeightPct = 0,
                OfficialSharesCount = 0,
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

    #region Active/pending with fingerprint idempotency

    private void ApplySnapshot(BasketSnapshot snapshot)
    {
        lock (_stateLock)
        {
            _state.LastError = null;
            var fp = snapshot.Fingerprint;

            if (_state.Active is null)
            {
                _state.Active = snapshot;
                _state.Pending = null;
                _state.PendingEffectiveAtUtc = null;
                _logger.LogInformation("Bootstrap: basket set as active ({Count} constituents, fp={FP})",
                    snapshot.Constituents.Count, fp);
                return;
            }

            if (fp == _state.Active.Fingerprint)
            {
                _logger.LogInformation("Fingerprint {FP} matches active basket — skipping pending creation", fp);
                return;
            }

            if (_state.Pending is not null && fp == _state.Pending.Fingerprint)
            {
                _logger.LogInformation("Fingerprint {FP} matches pending basket — no churn", fp);
                return;
            }

            _state.Pending = snapshot;
            _state.PendingEffectiveAtUtc = _market.NextMarketOpenUtc();
            _logger.LogInformation("Basket stored as pending (fp={FP}) for {EffectiveAt}",
                fp, _state.PendingEffectiveAtUtc);
        }
    }

    #endregion

    #region Source fetch helper with raw-cache fallback

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
                Origin = "live",
            });
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Live fetch failed for {Source}; trying raw cache", name);

            var cached = await _rawCache.LoadAsync<T>(name, ct);
            if (cached is not null)
            {
                _logger.LogInformation("Loaded {Source} from raw cache fallback", name);
                outcomes.Add(new SourceFetchOutcome
                {
                    SourceName = name,
                    Success = true,
                    FetchedAtUtc = DateTimeOffset.UtcNow,
                    RowCount = GetRowCount(cached),
                    Origin = "raw-cache",
                });
                return cached;
            }

            outcomes.Add(new SourceFetchOutcome
            {
                SourceName = name,
                Success = false,
                FetchedAtUtc = DateTimeOffset.UtcNow,
                Error = ex.Message,
                Origin = "failed",
            });
            return null;
        }
    }

    private static int GetRowCount(object result) => result switch
    {
        StockAnalysisAdapter.StockAnalysisResult sa => sa.Holdings.Count,
        SchwabHoldingsAdapter.SchwabFetchResult s => s.Constituents.Count,
        AlphaVantageAdapter.AlphaVantageResult a => a.FilteredCount,
        NasdaqHoldingsAdapter.NasdaqFetchResult n => n.Constituents.Count,
        _ => 0,
    };

    #endregion
}
