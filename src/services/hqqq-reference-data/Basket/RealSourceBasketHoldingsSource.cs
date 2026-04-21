using Hqqq.ReferenceData.Sources;

namespace Hqqq.ReferenceData.Basket;

/// <summary>
/// <see cref="IHoldingsSource"/> adapter that surfaces the ported Phase 1
/// basket pipeline to <see cref="BasketRefreshPipeline"/>. When a
/// pending merged basket is available it is returned; otherwise the
/// source reports <c>Unavailable</c> so the composite falls through to
/// the next arm (live file/http, then the deterministic seed).
/// </summary>
/// <remarks>
/// <para>
/// This keeps the existing <c>BasketRefreshPipeline</c> unchanged — it
/// continues to see a single <see cref="IHoldingsSource"/>, run
/// validation, apply corp-action adjustments, plan transitions, and
/// publish. The lifecycle scheduler is the only party that knows about
/// the fetch/merge/activate split; from the pipeline's point of view
/// every refresh is still "fetch → adjust → publish".
/// </para>
/// </remarks>
public sealed class RealSourceBasketHoldingsSource : IHoldingsSource
{
    private readonly PendingBasketStore _pending;
    private readonly RealSourceBasketPipeline _pipeline;
    private readonly ILogger<RealSourceBasketHoldingsSource> _logger;

    public RealSourceBasketHoldingsSource(
        PendingBasketStore pending,
        RealSourceBasketPipeline pipeline,
        ILogger<RealSourceBasketHoldingsSource> logger)
    {
        _pending = pending;
        _pipeline = pipeline;
        _logger = logger;
    }

    public string Name => "real-source-basket";

    public async Task<HoldingsFetchResult> FetchAsync(CancellationToken ct)
    {
        // Cold start: try to rehydrate pending from disk cache so the
        // first refresh after a restart does not forcibly fall back to
        // the seed.
        if (_pending.Pending is null)
        {
            await _pipeline.RecoverFromCacheAsync(ct).ConfigureAwait(false);
        }

        var envelope = _pending.Pending;
        if (envelope is null)
        {
            return HoldingsFetchResult.Unavailable(
                "no pending basket yet (lifecycle scheduler has not produced one)");
        }

        _logger.LogDebug(
            "RealSourceBasketHoldingsSource: returning pending basket {Fp16} count={Count} tail={Tail}",
            envelope.ContentFingerprint16, envelope.ConstituentCount, envelope.TailSource);

        return HoldingsFetchResult.Ok(envelope.Snapshot);
    }
}
