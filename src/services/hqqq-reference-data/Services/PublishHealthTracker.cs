namespace Hqqq.ReferenceData.Services;

/// <summary>
/// Thread-safe tracker for Kafka publish health. Updated by
/// <see cref="BasketRefreshPipeline"/> on every publish attempt/success/
/// failure and read by <see cref="Hqqq.ReferenceData.Health.ActiveBasketHealthCheck"/>,
/// <see cref="BasketService"/>, and the Prometheus metric callbacks.
///
/// Kept separate from <see cref="ActiveBasketStore"/> on purpose:
/// the basket record is immutable once activated, while publish health
/// is a separate operational concern that can (and must) drift
/// independently when the broker blips.
/// </summary>
public sealed class PublishHealthTracker
{
    private readonly object _lock = new();
    private PublishHealthSnapshot _state = PublishHealthSnapshot.Empty;

    /// <summary>Atomic snapshot of the current publish-health state.</summary>
    public PublishHealthSnapshot Snapshot
    {
        get
        {
            lock (_lock) { return _state; }
        }
    }

    /// <summary>
    /// Record that a publish attempt is about to happen. Always paired
    /// with either <see cref="RecordSuccess"/> or <see cref="RecordFailure"/>.
    /// </summary>
    public void RecordAttempt(DateTimeOffset nowUtc)
    {
        lock (_lock)
        {
            _state = _state with { LastPublishAttemptUtc = nowUtc };
        }
    }

    /// <summary>
    /// Record a successful publish: resets the consecutive-failure counter,
    /// advances <c>LastPublishOkUtc</c>, clears the last error, and
    /// remembers the fingerprint that just landed on the broker.
    /// </summary>
    public void RecordSuccess(DateTimeOffset nowUtc, string fingerprint)
    {
        lock (_lock)
        {
            _state = _state with
            {
                LastPublishOkUtc = nowUtc,
                ConsecutivePublishFailures = 0,
                LastPublishError = null,
                LastPublishedFingerprint = fingerprint,
            };
        }
    }

    /// <summary>
    /// Record a failed publish: increments the consecutive-failure counter,
    /// advances <c>LastPublishFailureUtc</c>, captures the error message.
    /// <c>LastPublishOkUtc</c> and <c>LastPublishedFingerprint</c> are
    /// preserved so the recovery window can be reasoned about.
    /// </summary>
    public void RecordFailure(DateTimeOffset nowUtc, string error)
    {
        lock (_lock)
        {
            _state = _state with
            {
                LastPublishFailureUtc = nowUtc,
                ConsecutivePublishFailures = _state.ConsecutivePublishFailures + 1,
                LastPublishError = error,
            };
        }
    }
}

/// <summary>
/// Immutable snapshot of Kafka publish health. Consumed by the readiness
/// check, the <c>/api/basket/current</c> response projection, and the
/// Prometheus observable gauges.
/// </summary>
public sealed record PublishHealthSnapshot
{
    public DateTimeOffset? LastPublishAttemptUtc { get; init; }
    public DateTimeOffset? LastPublishOkUtc { get; init; }
    public DateTimeOffset? LastPublishFailureUtc { get; init; }
    public int ConsecutivePublishFailures { get; init; }
    public string? LastPublishError { get; init; }
    public string? LastPublishedFingerprint { get; init; }

    public static readonly PublishHealthSnapshot Empty = new();
}
