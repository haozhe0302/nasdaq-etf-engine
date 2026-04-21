using Hqqq.ReferenceData.Sources;

namespace Hqqq.ReferenceData.Basket;

/// <summary>
/// In-memory holder for the pending (merged-but-not-yet-activated)
/// basket. Phase 2 analogue of the Phase 1 candidate/pending state
/// managed inside <c>BasketRefreshService</c>. Promotion from pending to
/// active is the <see cref="BasketLifecycleScheduler"/>'s responsibility.
/// </summary>
/// <remarks>
/// <para>
/// Lifecycle:
/// <list type="bullet">
///   <item>08:00 ET — raw sources fetched into <c>RawSourceCache</c>.</item>
///   <item>08:30 ET — merge builds a <see cref="MergedBasketEnvelope"/> and sets it here.</item>
///   <item>09:30 ET — if the market is open and the pending fingerprint
///   differs from the active fingerprint, the lifecycle scheduler runs
///   <c>BasketRefreshPipeline.RefreshAsync</c> which reads the pending
///   basket via <see cref="RealSourceBasketHoldingsSource"/>.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class PendingBasketStore
{
    private readonly object _lock = new();
    private MergedBasketEnvelope? _pending;
    private DateTimeOffset? _pendingEffectiveAtUtc;

    public MergedBasketEnvelope? Pending
    {
        get { lock (_lock) { return _pending; } }
    }

    public DateTimeOffset? PendingEffectiveAtUtc
    {
        get { lock (_lock) { return _pendingEffectiveAtUtc; } }
    }

    public void SetPending(MergedBasketEnvelope envelope, DateTimeOffset effectiveAtUtc)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        lock (_lock)
        {
            _pending = envelope;
            _pendingEffectiveAtUtc = effectiveAtUtc;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _pending = null;
            _pendingEffectiveAtUtc = null;
        }
    }
}
