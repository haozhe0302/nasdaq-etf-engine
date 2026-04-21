namespace Hqqq.Ingress.State;

/// <summary>
/// Thread-safe snapshot of the currently active symbol set for the Tiingo
/// websocket subscription. Populated by <see cref="Consumers.BasketActiveConsumer"/>
/// from <c>refdata.basket.active.v1</c> and consumed by the
/// <see cref="BasketSubscriptionCoordinator"/> + worker.
/// </summary>
/// <remarks>
/// <para>
/// Reads (<see cref="Current"/>) are lock-free via volatile reference
/// snapshot. Writes (<see cref="SetFromBasket"/>) atomically replace the
/// reference; callers that care about ordering can gate on the
/// <see cref="BasketUpdated"/> event.
/// </para>
/// </remarks>
public sealed class ActiveSymbolUniverse
{
    private volatile UniverseSnapshot? _current;

    /// <summary>
    /// Fires after every <see cref="SetFromBasket"/> call that delivers a
    /// genuinely new fingerprint. Arguments are the new snapshot only —
    /// the subscriber owns the diff with whatever it last applied.
    /// </summary>
    public event Action<UniverseSnapshot>? BasketUpdated;

    /// <summary>Returns the latest snapshot or <c>null</c> before the first basket arrives.</summary>
    public UniverseSnapshot? Current => _current;

    /// <summary>Shortcut for <c>Current is not null</c>; safe to call from any thread.</summary>
    public bool HasBasket => _current is not null;

    /// <summary>
    /// Atomically swaps the active universe. Idempotent when <paramref name="fingerprint"/>
    /// matches the current snapshot (no event fires). Symbols are normalized
    /// to upper-case and deduplicated before being stored.
    /// </summary>
    public void SetFromBasket(
        string basketId,
        string fingerprint,
        DateOnly asOfDate,
        IEnumerable<string> symbols,
        string source,
        DateTimeOffset updatedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basketId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        ArgumentNullException.ThrowIfNull(symbols);

        var normalized = symbols
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);

        var existing = _current;
        if (existing is not null && existing.Fingerprint == fingerprint)
        {
            // Republish of same basket — refresh the timestamp so health
            // probes see recent basket activity, but don't re-trigger
            // subscribe/unsubscribe.
            _current = existing with { UpdatedAtUtc = updatedAtUtc };
            return;
        }

        var snapshot = new UniverseSnapshot
        {
            BasketId = basketId,
            Fingerprint = fingerprint,
            AsOfDate = asOfDate,
            Symbols = normalized,
            Source = source,
            UpdatedAtUtc = updatedAtUtc,
        };

        _current = snapshot;
        BasketUpdated?.Invoke(snapshot);
    }
}

/// <summary>Immutable snapshot of the active symbol universe at a point in time.</summary>
public sealed record UniverseSnapshot
{
    public required string BasketId { get; init; }
    public required string Fingerprint { get; init; }
    public required DateOnly AsOfDate { get; init; }
    public required IReadOnlySet<string> Symbols { get; init; }
    public required string Source { get; init; }
    public required DateTimeOffset UpdatedAtUtc { get; init; }
}
