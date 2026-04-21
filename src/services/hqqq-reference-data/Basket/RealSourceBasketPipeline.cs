using Hqqq.ReferenceData.Configuration;
using Hqqq.ReferenceData.Sources;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Basket;

/// <summary>
/// Phase 2 port of <c>src/hqqq-api/Modules/Basket/Services/BasketSnapshotProvider.cs</c>.
/// Drives the four-source anchored basket pipeline (StockAnalysis /
/// Schwab anchors + AlphaVantage / Nasdaq tail) into a pending
/// <see cref="MergedBasketEnvelope"/>.
/// </summary>
/// <remarks>
/// <para>
/// Stages (mirrors Phase 1):
/// <list type="number">
///   <item><see cref="FetchAsync"/> — for every enabled adapter: live fetch → on success write to <see cref="RawSourceCache"/>, record a <see cref="SourceFetchOutcome"/>; on failure leave the previous cache in place so <see cref="MergeAsync"/> still has something to work with.</item>
///   <item><see cref="MergeAsync"/> — load every cached payload; pick the freshest anchor (StockAnalysis vs Schwab, tie → StockAnalysis); build the tail (AlphaVantage if present, else Nasdaq proxy); filter tail through the Nasdaq universe guardrail; call <see cref="MergedBasketBuilder.BuildAnchored"/> and publish to <see cref="PendingBasketStore"/> + <see cref="MergedBasketCache"/>.</item>
///   <item><see cref="EnsurePendingAsync"/> — if no pending basket exists yet, run <see cref="FetchAsync"/>+<see cref="MergeAsync"/> immediately so <see cref="BasketRefreshPipeline"/> never has to fall through to the seed during cold start.</item>
///   <item><see cref="RecoverFromCacheAsync"/> — warm-start: re-hydrate pending from the on-disk merged cache without a fresh upstream fetch.</item>
/// </list>
/// </para>
/// <para>
/// The activation/publish stage is intentionally delegated to the
/// existing <c>BasketRefreshPipeline</c> so corp-action adjustment,
/// transition planning, and Kafka publish stay on a single code path.
/// </para>
/// </remarks>
public sealed class RealSourceBasketPipeline
{
    private readonly StockAnalysisBasketAdapter _stockAnalysis;
    private readonly SchwabBasketAdapter _schwab;
    private readonly AlphaVantageBasketAdapter _alpha;
    private readonly NasdaqBasketAdapter _nasdaq;
    private readonly RawSourceCache _rawCache;
    private readonly MergedBasketCache _mergedCache;
    private readonly PendingBasketStore _pending;
    private readonly BasketOptions _basketOptions;
    private readonly IWebHostEnvironment _environment;
    private readonly TimeProvider _clock;
    private readonly ILogger<RealSourceBasketPipeline> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RealSourceBasketPipeline(
        StockAnalysisBasketAdapter stockAnalysis,
        SchwabBasketAdapter schwab,
        AlphaVantageBasketAdapter alpha,
        NasdaqBasketAdapter nasdaq,
        RawSourceCache rawCache,
        MergedBasketCache mergedCache,
        PendingBasketStore pending,
        IOptions<ReferenceDataOptions> options,
        IWebHostEnvironment environment,
        ILogger<RealSourceBasketPipeline> logger,
        TimeProvider? clock = null)
    {
        _stockAnalysis = stockAnalysis;
        _schwab = schwab;
        _alpha = alpha;
        _nasdaq = nasdaq;
        _rawCache = rawCache;
        _mergedCache = mergedCache;
        _pending = pending;
        _basketOptions = options.Value.Basket;
        _environment = environment;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>Stage 1: fetch each enabled adapter and write successful payloads to the raw cache.</summary>
    public async Task<FetchSummary> FetchAsync(CancellationToken ct)
    {
        var stockAnalysisOutcome = await _stockAnalysis.FetchAsync(ct).ConfigureAwait(false);
        if (stockAnalysisOutcome.Success && stockAnalysisOutcome.Payload is not null)
        {
            await _rawCache.WriteAsync(_stockAnalysis.Name, stockAnalysisOutcome.Payload, ct)
                .ConfigureAwait(false);
        }

        var schwabOutcome = await _schwab.FetchAsync(ct).ConfigureAwait(false);
        if (schwabOutcome.Success && schwabOutcome.Payload is not null)
        {
            await _rawCache.WriteAsync(_schwab.Name, schwabOutcome.Payload, ct)
                .ConfigureAwait(false);
        }

        var alphaOutcome = await _alpha.FetchAsync(ct).ConfigureAwait(false);
        if (alphaOutcome.Success && alphaOutcome.Payload is not null)
        {
            await _rawCache.WriteAsync(_alpha.Name, alphaOutcome.Payload, ct)
                .ConfigureAwait(false);
        }

        var nasdaqOutcome = await _nasdaq.FetchAsync(ct).ConfigureAwait(false);
        if (nasdaqOutcome.Success && nasdaqOutcome.Payload is not null)
        {
            await _rawCache.WriteAsync(_nasdaq.Name, nasdaqOutcome.Payload, ct)
                .ConfigureAwait(false);
        }

        _logger.LogInformation(
            "RealSourceBasketPipeline.Fetch: stockanalysis={SA} schwab={SC} alphavantage={AV} nasdaq={ND}",
            Label(stockAnalysisOutcome), Label(schwabOutcome),
            Label(alphaOutcome), Label(nasdaqOutcome));

        return new FetchSummary(stockAnalysisOutcome, schwabOutcome, alphaOutcome, nasdaqOutcome);

        static string Label<T>(BasketSourceOutcome<T> o) where T : class
            => $"{o.Origin}({o.RowCount})";
    }

    /// <summary>Stage 2: merge the cached raw payloads into a pending basket envelope.</summary>
    public async Task<MergeOutcome> MergeAsync(CancellationToken ct)
    {
        var stockAnalysis = await _rawCache
            .TryReadAsync<StockAnalysisBasketAdapter.RawResult>(_stockAnalysis.Name, ct)
            .ConfigureAwait(false);
        var schwab = await _rawCache
            .TryReadAsync<SchwabBasketAdapter.RawResult>(_schwab.Name, ct)
            .ConfigureAwait(false);
        var alpha = await _rawCache
            .TryReadAsync<AlphaVantageBasketAdapter.RawResult>(_alpha.Name, ct)
            .ConfigureAwait(false);
        var nasdaq = await _rawCache
            .TryReadAsync<NasdaqBasketAdapter.RawResult>(_nasdaq.Name, ct)
            .ConfigureAwait(false);

        // ── Universe guardrail = Nasdaq constituents. ────────────────
        HashSet<string>? universe = null;
        if (nasdaq?.Entries is { Count: > 0 })
        {
            universe = new HashSet<string>(
                nasdaq.Entries.Select(e => e.Symbol),
                StringComparer.OrdinalIgnoreCase);
        }

        // ── Anchor selection: newer AsOfDate wins; tie → StockAnalysis. ─
        var anchor = BuildAnchor(stockAnalysis, schwab);

        // ── Tail selection: AlphaVantage (if any) → else Nasdaq proxy. ─
        var tail = BuildTail(alpha, nasdaq);

        if (anchor is not null && tail is not null)
        {
            var merged = MergedBasketBuilder.BuildAnchored(
                anchor, tail, universe,
                basketId: "HQQQ",
                version: $"v-{anchor.AsOfDate:yyyyMMdd}-{anchor.SourceName}+{tail.SourceName}");

            var envelope = new MergedBasketEnvelope
            {
                Snapshot = merged.Snapshot,
                MergedAtUtc = _clock.GetUtcNow(),
                TailSource = tail.SourceName,
                IsDegraded = merged.Quality.IsDegraded,
                ContentFingerprint16 = merged.Quality.ContentFingerprint16,
                ConstituentCount = merged.Quality.FinalSymbolCount,
                AnchorSource = merged.Quality.AnchorSource,
                HasOfficialShares = merged.Quality.HasOfficialShares,
                BasketMode = merged.Quality.BasketMode,
            };

            await _mergedCache.StoreAsync(envelope, ct).ConfigureAwait(false);
            _pending.SetPending(envelope, envelope.MergedAtUtc);

            _logger.LogInformation(
                "RealSourceBasketPipeline.Merge: anchored basket {Fp16} anchor={Anchor} tail={Tail} count={Count} degraded={Degraded} officialShares={Shares}",
                envelope.ContentFingerprint16, envelope.AnchorSource, envelope.TailSource,
                envelope.ConstituentCount, envelope.IsDegraded, envelope.HasOfficialShares);

            return MergeOutcome.Ok(envelope);
        }

        // ── Degraded anchor-less fallback. ────────────────────────────
        if (tail is null)
        {
            _logger.LogWarning(
                "RealSourceBasketPipeline.Merge: no usable tail payload (alpha={Alpha}, nasdaq={Nasdaq})",
                alpha is null ? "missing" : "ok",
                nasdaq is null ? "missing" : "ok");
            return MergeOutcome.NoSources;
        }

        // We have a tail but no anchor. This is the anchor-less proxy
        // posture — allowed only outside Production or when the operator
        // has explicitly opted in. The calling background service must
        // NOT publish this into active unless the production-guard door
        // is open.
        if (_environment.IsProduction() && !_basketOptions.AllowAnchorlessProxyInProduction)
        {
            _logger.LogWarning(
                "RealSourceBasketPipeline.Merge: anchor unavailable in Production and AllowAnchorlessProxyInProduction=false; refusing to emit anchor-less pending basket");
            return MergeOutcome.AnchorRequired;
        }

        var proxy = MergedBasketBuilder.Build(
            tail, universe,
            basketId: "HQQQ",
            version: $"v-{tail.AsOfDate:yyyyMMdd}-{tail.SourceName}");

        var proxyEnvelope = new MergedBasketEnvelope
        {
            Snapshot = proxy.Snapshot,
            MergedAtUtc = _clock.GetUtcNow(),
            TailSource = tail.SourceName,
            IsDegraded = proxy.Quality.IsDegraded,
            ContentFingerprint16 = proxy.Quality.ContentFingerprint16,
            ConstituentCount = proxy.Quality.FinalSymbolCount,
            AnchorSource = null,
            HasOfficialShares = false,
            BasketMode = proxy.Quality.BasketMode,
        };

        await _mergedCache.StoreAsync(proxyEnvelope, ct).ConfigureAwait(false);
        _pending.SetPending(proxyEnvelope, proxyEnvelope.MergedAtUtc);

        _logger.LogWarning(
            "RealSourceBasketPipeline.Merge: anchor-less proxy basket {Fp16} tail={Tail} count={Count} (explicit degraded posture)",
            proxyEnvelope.ContentFingerprint16, proxyEnvelope.TailSource,
            proxyEnvelope.ConstituentCount);

        return MergeOutcome.Ok(proxyEnvelope);
    }

    /// <summary>
    /// Phase-1-equivalent cold-start cycle: if no pending basket exists
    /// yet, run fetch + merge immediately so <c>BasketRefreshPipeline</c>
    /// has a real-source candidate to adjust + publish instead of
    /// falling through to the seed. Serialized under a semaphore so
    /// concurrent startup + scheduler calls do not interleave.
    /// </summary>
    public async Task<MergeOutcome> EnsurePendingAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_pending.Pending is not null)
            {
                return MergeOutcome.Ok(_pending.Pending);
            }

            // Prefer warm-start from disk cache when present — a restart
            // in the middle of a trading day should not require a fresh
            // upstream fetch to serve the basket again.
            var recovered = await _mergedCache.TryLoadAsync(ct).ConfigureAwait(false);
            if (recovered is not null)
            {
                _pending.SetPending(recovered, recovered.MergedAtUtc);
                _logger.LogInformation(
                    "RealSourceBasketPipeline.EnsurePending: restored {Fp16} from merged cache",
                    recovered.ContentFingerprint16);
                return MergeOutcome.Ok(recovered);
            }

            await FetchAsync(ct).ConfigureAwait(false);
            return await MergeAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Warm-start: if a merged envelope exists on disk, load it as pending (without re-fetching).</summary>
    public async Task RecoverFromCacheAsync(CancellationToken ct)
    {
        if (_pending.Pending is not null) return;
        var envelope = await _mergedCache.TryLoadAsync(ct).ConfigureAwait(false);
        if (envelope is null) return;

        _pending.SetPending(envelope, envelope.MergedAtUtc);
        _logger.LogInformation(
            "RealSourceBasketPipeline.Recover: restored pending basket {Fp16} from disk cache",
            envelope.ContentFingerprint16);
    }

    private MergedBasketBuilder.AnchorBlock? BuildAnchor(
        StockAnalysisBasketAdapter.RawResult? sa,
        SchwabBasketAdapter.RawResult? schwab)
    {
        var saOk = sa is { Holdings.Count: > 0 };
        var schwabOk = schwab is { Holdings.Count: > 0 };

        if (saOk && schwabOk)
        {
            // Newer snapshot wins; ties go to StockAnalysis (broader top-N
            // coverage and a cleaner explicit as-of date).
            var saDate = sa!.AsOfDate;
            var schwabDate = schwab!.AsOfDate;

            if (saDate > schwabDate) return BuildStockAnalysisAnchor(sa);
            if (schwabDate > saDate) return BuildSchwabAnchor(schwab);
            return BuildStockAnalysisAnchor(sa);
        }

        if (saOk) return BuildStockAnalysisAnchor(sa!);
        if (schwabOk) return BuildSchwabAnchor(schwab!);

        return null;
    }

    private static MergedBasketBuilder.AnchorBlock BuildStockAnalysisAnchor(
        StockAnalysisBasketAdapter.RawResult sa)
    {
        var entries = sa.Holdings
            .Select(h => new MergedBasketBuilder.AnchorEntry(
                Symbol: h.Symbol,
                Name: h.Name,
                Sector: "Unknown",
                SharesHeld: h.Shares,
                RawWeight: h.WeightPct))
            .ToList();
        return new MergedBasketBuilder.AnchorBlock(
            entries, StockAnalysisBasketAdapter.AdapterName, sa.AsOfDate);
    }

    private static MergedBasketBuilder.AnchorBlock BuildSchwabAnchor(
        SchwabBasketAdapter.RawResult schwab)
    {
        var entries = schwab.Holdings
            .Select(h => new MergedBasketBuilder.AnchorEntry(
                Symbol: h.Symbol,
                Name: h.Description,
                Sector: "Unknown",
                SharesHeld: h.SharesHeld,
                RawWeight: h.WeightPct))
            .ToList();
        return new MergedBasketBuilder.AnchorBlock(
            entries, SchwabBasketAdapter.AdapterName, schwab.AsOfDate);
    }

    private MergedBasketBuilder.TailBlock? BuildTail(
        AlphaVantageBasketAdapter.RawResult? alpha,
        NasdaqBasketAdapter.RawResult? nasdaq)
    {
        if (alpha?.Holdings is { Count: > 0 })
        {
            var entries = alpha.Holdings
                .Select(h => new MergedBasketBuilder.TailEntry(
                    h.Symbol, h.Description, h.Weight, "Unknown"))
                .ToList();
            var asOf = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
            return new MergedBasketBuilder.TailBlock(
                entries, AlphaVantageBasketAdapter.AdapterName, IsProxy: false, asOf);
        }

        if (nasdaq?.Entries is { Count: > 0 })
        {
            var entries = nasdaq.Entries
                .Select(e => new MergedBasketBuilder.TailEntry(
                    e.Symbol, e.CompanyName, e.Weight, "Unknown"))
                .ToList();
            var asOf = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
            return new MergedBasketBuilder.TailBlock(
                entries, NasdaqBasketAdapter.AdapterName, IsProxy: true, asOf);
        }

        return null;
    }

    public sealed record FetchSummary(
        BasketSourceOutcome<StockAnalysisBasketAdapter.RawResult> StockAnalysis,
        BasketSourceOutcome<SchwabBasketAdapter.RawResult> Schwab,
        BasketSourceOutcome<AlphaVantageBasketAdapter.RawResult> AlphaVantage,
        BasketSourceOutcome<NasdaqBasketAdapter.RawResult> Nasdaq);

    public sealed record MergeOutcome(bool Success, MergedBasketEnvelope? Envelope, string? Reason)
    {
        public static readonly MergeOutcome NoSources =
            new(false, null, "no usable raw payload");

        public static readonly MergeOutcome AnchorRequired =
            new(false, null, "anchor unavailable and AllowAnchorlessProxyInProduction=false");

        public static MergeOutcome Ok(MergedBasketEnvelope envelope) =>
            new(true, envelope, null);
    }
}
