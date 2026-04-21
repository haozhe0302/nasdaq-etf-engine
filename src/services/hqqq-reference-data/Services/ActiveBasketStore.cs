using Hqqq.ReferenceData.Sources;

namespace Hqqq.ReferenceData.Services;

/// <summary>
/// In-memory holder of the currently-active basket snapshot + its
/// fingerprint + the moment it was activated. Single source of truth for
/// <c>GET /api/basket/current</c>, the readiness health check, and the
/// Kafka publisher. Thread-safe; immutable swaps under a lock.
/// </summary>
public sealed class ActiveBasketStore
{
    private readonly object _lock = new();
    private ActiveBasket? _current;

    /// <summary>Returns the currently-active basket, or null before the first refresh.</summary>
    public ActiveBasket? Current
    {
        get
        {
            lock (_lock) { return _current; }
        }
    }

    /// <summary>
    /// Atomically replaces the current active basket. The fingerprint is
    /// computed by the caller (the refresh pipeline) so the same value is
    /// used both for the in-memory store and the published Kafka event.
    /// </summary>
    public void Set(ActiveBasket next)
    {
        ArgumentNullException.ThrowIfNull(next);
        lock (_lock) { _current = next; }
    }
}

/// <summary>
/// Snapshot+metadata pair produced by the refresh pipeline. Carries enough
/// for the API surface, health check, and Kafka publisher to render their
/// own views without recomputing anything.
/// </summary>
public sealed record ActiveBasket
{
    public required HoldingsSnapshot Snapshot { get; init; }
    public required string Fingerprint { get; init; }
    public required DateTimeOffset ActivatedAtUtc { get; init; }
}
