using Hqqq.Contracts.Events;

namespace Hqqq.QuoteEngine.State;

/// <summary>
/// Lightweight crash-recovery checkpoint persisted by the engine. Carries
/// just enough state for the restorer to re-install the active basket
/// (identity + pricing basis + scale factor) and surface the last computed
/// snapshot digest; full raw-tick history is intentionally not persisted.
/// </summary>
public sealed record EngineCheckpoint
{
    /// <summary>Schema version. Bumped on any breaking checkpoint change.</summary>
    public int SchemaVersion { get; init; } = 1;

    public DateTimeOffset WrittenAtUtc { get; init; }

    /// <summary>
    /// Full active-basket payload, reused from the Kafka contract shape so
    /// restore and live-consume paths share a single mapping.
    /// </summary>
    public required BasketActiveStateV1 Basket { get; init; }

    /// <summary>Last materialized snapshot digest, or null if nothing materialized yet.</summary>
    public SnapshotDigest? LastSnapshot { get; init; }
}

/// <summary>
/// Small, bounded summary of the most recently materialized snapshot.
/// Persisted alongside the basket to aid observability / diagnostics after
/// restart; not used to drive pricing math.
/// </summary>
public sealed record SnapshotDigest
{
    public required decimal Nav { get; init; }
    public required decimal Qqq { get; init; }
    public required decimal PremiumDiscountPct { get; init; }
    public required DateTimeOffset ComputedAtUtc { get; init; }
}
