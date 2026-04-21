using Hqqq.ReferenceData.Configuration;
using Hqqq.ReferenceData.Sources;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Basket;

/// <summary>
/// Phase 2 port of <c>BasketRefreshService</c> from the Phase 1 Basket
/// module. Runs the two IO-bound lifecycle stages that happen before
/// the market-open activation:
/// <list type="number">
///   <item><c>FetchAsync</c> — pull AlphaVantage + Nasdaq JSON into the raw-source cache.</item>
///   <item><c>MergeAsync</c> — project cached raw payloads into a <see cref="MergedBasketEnvelope"/> via <see cref="MergedBasketBuilder"/> and set the pending basket.</item>
/// </list>
/// The activation/publish stage is intentionally delegated to the
/// existing <c>BasketRefreshPipeline</c> so corp-action adjustment,
/// transition planning, and Kafka publish stay on a single code path.
/// </summary>
public sealed class RealSourceBasketPipeline
{
    private readonly AlphaVantageBasketAdapter _alpha;
    private readonly NasdaqBasketAdapter _nasdaq;
    private readonly RawSourceCache _rawCache;
    private readonly MergedBasketCache _mergedCache;
    private readonly PendingBasketStore _pending;
    private readonly BasketOptions _basketOptions;
    private readonly TimeProvider _clock;
    private readonly ILogger<RealSourceBasketPipeline> _logger;

    public RealSourceBasketPipeline(
        AlphaVantageBasketAdapter alpha,
        NasdaqBasketAdapter nasdaq,
        RawSourceCache rawCache,
        MergedBasketCache mergedCache,
        PendingBasketStore pending,
        IOptions<ReferenceDataOptions> options,
        ILogger<RealSourceBasketPipeline> logger,
        TimeProvider? clock = null)
    {
        _alpha = alpha;
        _nasdaq = nasdaq;
        _rawCache = rawCache;
        _mergedCache = mergedCache;
        _pending = pending;
        _basketOptions = options.Value.Basket;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>Stage 1: fetch each enabled adapter and write successful payloads to the raw cache.</summary>
    public async Task<FetchSummary> FetchAsync(CancellationToken ct)
    {
        var alphaOutcome = await _alpha.FetchAsync(ct).ConfigureAwait(false);
        if (alphaOutcome.Success && alphaOutcome.Payload is not null)
        {
            await _rawCache.WriteAsync(_alpha.Name, alphaOutcome.Payload, ct).ConfigureAwait(false);
        }

        var nasdaqOutcome = await _nasdaq.FetchAsync(ct).ConfigureAwait(false);
        if (nasdaqOutcome.Success && nasdaqOutcome.Payload is not null)
        {
            await _rawCache.WriteAsync(_nasdaq.Name, nasdaqOutcome.Payload, ct).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "RealSourceBasketPipeline.Fetch: alphavantage={AlphaOrigin} ({AlphaRows}), nasdaq={NasdaqOrigin} ({NasdaqRows})",
            alphaOutcome.Origin, alphaOutcome.RowCount,
            nasdaqOutcome.Origin, nasdaqOutcome.RowCount);

        return new FetchSummary(alphaOutcome, nasdaqOutcome);
    }

    /// <summary>Stage 2: merge the cached raw payloads into a pending basket envelope.</summary>
    public async Task<MergeOutcome> MergeAsync(CancellationToken ct)
    {
        var alpha = await _rawCache.TryReadAsync<AlphaVantageBasketAdapter.RawResult>(_alpha.Name, ct)
            .ConfigureAwait(false);
        var nasdaq = await _rawCache.TryReadAsync<NasdaqBasketAdapter.RawResult>(_nasdaq.Name, ct)
            .ConfigureAwait(false);

        HashSet<string>? universe = null;
        if (nasdaq?.Entries is { Count: > 0 })
        {
            universe = new HashSet<string>(
                nasdaq.Entries.Select(e => e.Symbol),
                StringComparer.OrdinalIgnoreCase);
        }

        MergedBasketBuilder.TailBlock? tail = null;
        if (alpha?.Holdings is { Count: > 0 })
        {
            tail = new MergedBasketBuilder.TailBlock(
                alpha.Holdings.Select(h => new MergedBasketBuilder.TailEntry(
                    h.Symbol, h.Description, h.Weight, "Unknown")).ToList(),
                SourceName: _alpha.Name,
                IsProxy: false,
                AsOfDate: DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime));
        }
        else if (nasdaq?.Entries is { Count: > 0 })
        {
            // AlphaVantage absent — degrade to Nasdaq market-cap proxy.
            tail = new MergedBasketBuilder.TailBlock(
                nasdaq.Entries.Select(e => new MergedBasketBuilder.TailEntry(
                    e.Symbol, e.CompanyName, e.Weight, "Unknown")).ToList(),
                SourceName: _nasdaq.Name,
                IsProxy: true,
                AsOfDate: DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime));
        }

        if (tail is null)
        {
            _logger.LogWarning(
                "RealSourceBasketPipeline.Merge: no usable raw payload (alpha={Alpha}, nasdaq={Nasdaq})",
                alpha is null ? "missing" : "ok",
                nasdaq is null ? "missing" : "ok");
            return MergeOutcome.NoSources;
        }

        var merged = MergedBasketBuilder.Build(
            tail,
            universe,
            basketId: "HQQQ",
            version: $"v-{tail.AsOfDate:yyyyMMdd}-{tail.SourceName}");

        var envelope = new MergedBasketEnvelope
        {
            Snapshot = merged.Snapshot,
            MergedAtUtc = _clock.GetUtcNow(),
            TailSource = tail.SourceName,
            IsDegraded = merged.Quality.IsDegraded,
            ContentFingerprint16 = merged.Quality.ContentFingerprint16,
            ConstituentCount = merged.Quality.FinalSymbolCount,
        };

        await _mergedCache.StoreAsync(envelope, ct).ConfigureAwait(false);
        _pending.SetPending(envelope, envelope.MergedAtUtc);

        _logger.LogInformation(
            "RealSourceBasketPipeline.Merge: pending basket {Fp16} tail={Tail} count={Count} degraded={Degraded}",
            envelope.ContentFingerprint16, envelope.TailSource,
            envelope.ConstituentCount, envelope.IsDegraded);

        return MergeOutcome.Ok(envelope);
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

    public sealed record FetchSummary(
        BasketSourceOutcome<AlphaVantageBasketAdapter.RawResult> AlphaVantage,
        BasketSourceOutcome<NasdaqBasketAdapter.RawResult> Nasdaq);

    public sealed record MergeOutcome(bool Success, MergedBasketEnvelope? Envelope, string? Reason)
    {
        public static readonly MergeOutcome NoSources =
            new(false, null, "no usable raw payload");

        public static MergeOutcome Ok(MergedBasketEnvelope envelope) =>
            new(true, envelope, null);
    }
}
