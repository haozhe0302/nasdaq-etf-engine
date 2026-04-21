using Hqqq.ReferenceData.CorporateActions.Contracts;
using Hqqq.ReferenceData.Sources;

namespace Hqqq.ReferenceData.Services;

/// <summary>
/// In-memory holder of the currently-active basket snapshot + its
/// fingerprint + the moment it was activated. Single source of truth for
/// <c>GET /api/basket/current</c>, the readiness health check, and the
/// Kafka publisher. Also retains the previous basket + latest adjustment
/// report so downstream consumers can reason about transitions. Thread-
/// safe; immutable swaps under a lock.
/// </summary>
public sealed class ActiveBasketStore
{
    private readonly object _lock = new();
    private ActiveBasket? _current;
    private ActiveBasket? _previous;
    private AdjustmentReport? _latestReport;

    /// <summary>Returns the currently-active basket, or null before the first refresh.</summary>
    public ActiveBasket? Current
    {
        get { lock (_lock) { return _current; } }
    }

    /// <summary>The basket that was active before the most recent activation, or <c>null</c>.</summary>
    public ActiveBasket? Previous
    {
        get { lock (_lock) { return _previous; } }
    }

    /// <summary>
    /// Report from the most recent corporate-action + transition pass
    /// (may be an empty report when no corp-actions fired). Surfaced
    /// verbatim on <c>GET /api/basket/current</c>.
    /// </summary>
    public AdjustmentReport? LatestAdjustmentReport
    {
        get { lock (_lock) { return _latestReport; } }
    }

    /// <summary>
    /// Atomically replaces the current active basket, captures the
    /// outgoing basket as <see cref="Previous"/>, and stores the
    /// adjustment report that produced this activation.
    /// </summary>
    public void Set(ActiveBasket next, AdjustmentReport? report)
    {
        ArgumentNullException.ThrowIfNull(next);
        lock (_lock)
        {
            _previous = _current;
            _current = next;
            _latestReport = report;
        }
    }

    /// <summary>
    /// Overwrites the stored adjustment report without changing the
    /// current basket. Used by the republish path to refresh the
    /// report's <c>AppliedAtUtc</c> (or clear it) without churning the
    /// active basket.
    /// </summary>
    public void UpdateReport(AdjustmentReport? report)
    {
        lock (_lock) { _latestReport = report; }
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
