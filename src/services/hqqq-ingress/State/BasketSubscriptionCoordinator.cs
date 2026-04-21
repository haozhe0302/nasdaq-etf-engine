using Hqqq.Ingress.Clients;

namespace Hqqq.Ingress.State;

/// <summary>
/// Bridges <see cref="ActiveSymbolUniverse"/> into the Tiingo websocket
/// subscription. Listens for <see cref="ActiveSymbolUniverse.BasketUpdated"/>
/// and applies the diff (add/remove) against the Tiingo client. Keeps its
/// own in-memory record of the most recently applied symbol set so
/// replaying a basket with the same fingerprint after reconnect is a no-op.
/// </summary>
/// <remarks>
/// The coordinator does not own the websocket lifecycle — it assumes the
/// worker opens/closes the socket and handles reconnect backoff. When the
/// socket is not open the client records the pending add/remove set and
/// the next <see cref="Workers.TiingoIngressWorker"/> connect seeds with
/// <see cref="CurrentAppliedSymbols"/>.
/// </remarks>
public sealed class BasketSubscriptionCoordinator : IDisposable
{
    private readonly ActiveSymbolUniverse _universe;
    private readonly ITiingoStreamClient _client;
    private readonly ILogger<BasketSubscriptionCoordinator> _logger;
    private readonly object _lock = new();
    private HashSet<string> _applied = new(StringComparer.Ordinal);
    private string? _appliedFingerprint;
    private DateTimeOffset? _lastAppliedUtc;
    private int _disposed;

    public BasketSubscriptionCoordinator(
        ActiveSymbolUniverse universe,
        ITiingoStreamClient client,
        ILogger<BasketSubscriptionCoordinator> logger)
    {
        _universe = universe;
        _client = client;
        _logger = logger;
        _universe.BasketUpdated += OnBasketUpdated;
    }

    /// <summary>
    /// Upper-case snapshot of the symbols currently believed to be
    /// subscribed on the wire. Thread-safe clone so callers can iterate
    /// without holding the internal lock.
    /// </summary>
    public IReadOnlyCollection<string> CurrentAppliedSymbols
    {
        get { lock (_lock) return _applied.ToArray(); }
    }

    /// <summary>Fingerprint of the most recently applied basket, if any.</summary>
    public string? AppliedFingerprint
    {
        get { lock (_lock) return _appliedFingerprint; }
    }

    /// <summary>Last time a basket snapshot was applied to the wire (successfully or attempted).</summary>
    public DateTimeOffset? LastAppliedUtc
    {
        get { lock (_lock) return _lastAppliedUtc; }
    }

    /// <summary>
    /// Seeds the coordinator with a bootstrap-override symbol set (from
    /// <c>Tiingo:Symbols</c>) before any basket event arrives. Idempotent.
    /// </summary>
    public void SeedBootstrapSymbols(IEnumerable<string> symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        var upper = symbols
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.Ordinal);

        lock (_lock)
        {
            if (_appliedFingerprint is not null) return; // a basket already won; ignore bootstrap
            if (upper.Count == 0) return;
            _applied = upper;
            _appliedFingerprint = "bootstrap:override";
            _lastAppliedUtc = DateTimeOffset.UtcNow;
        }
        _logger.LogInformation(
            "BasketSubscriptionCoordinator: seeded with {Count} bootstrap-override symbols",
            upper.Count);
    }

    private void OnBasketUpdated(UniverseSnapshot snapshot)
    {
        _ = ApplyAsync(snapshot, CancellationToken.None);
    }

    /// <summary>
    /// Applies the diff between <paramref name="snapshot"/> and the last
    /// applied set to the Tiingo client. Safe to call concurrently;
    /// serialised under the coordinator's lock.
    /// </summary>
    public async Task ApplyAsync(UniverseSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        string[] toAdd;
        string[] toRemove;
        lock (_lock)
        {
            if (_appliedFingerprint == snapshot.Fingerprint)
            {
                _lastAppliedUtc = DateTimeOffset.UtcNow;
                return;
            }

            toAdd = snapshot.Symbols.Except(_applied, StringComparer.Ordinal).ToArray();
            toRemove = _applied.Except(snapshot.Symbols, StringComparer.Ordinal).ToArray();

            _applied = new HashSet<string>(snapshot.Symbols, StringComparer.Ordinal);
            _appliedFingerprint = snapshot.Fingerprint;
            _lastAppliedUtc = DateTimeOffset.UtcNow;
        }

        try
        {
            if (toRemove.Length > 0)
                await _client.UnsubscribeAsync(toRemove, ct).ConfigureAwait(false);
            if (toAdd.Length > 0)
                await _client.SubscribeAsync(toAdd, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Basket applied: fingerprint={Fingerprint} added={Added} removed={Removed} total={Total}",
                snapshot.Fingerprint, toAdd.Length, toRemove.Length, snapshot.Symbols.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "BasketSubscriptionCoordinator: failed to apply basket diff; will retry on reconnect");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _universe.BasketUpdated -= OnBasketUpdated;
    }
}
