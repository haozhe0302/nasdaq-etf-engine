using Hqqq.Contracts.Events;

namespace Hqqq.Ingress.Clients;

/// <summary>
/// Abstraction over the Tiingo IEX WebSocket streaming connection.
/// Implementations connect, subscribe to the supplied symbol set, and
/// invoke <paramref name="onTick"/> for every parsed market tick until
/// the supplied cancellation token fires or the connection drops.
/// </summary>
public interface ITiingoStreamClient
{
    /// <summary>True when the underlying socket is open and authenticated.</summary>
    bool IsConnected { get; }

    /// <summary>UTC timestamp of the last successfully parsed tick (null when none).</summary>
    DateTimeOffset? LastDataUtc { get; }

    /// <summary>
    /// Connects, subscribes to <paramref name="symbols"/>, and invokes
    /// <paramref name="onTick"/> for every parsed tick. Returns when
    /// <paramref name="ct"/> is cancelled or the upstream closes the
    /// socket. Implementations should not retry internally — the caller
    /// owns the reconnect/backoff loop.
    /// </summary>
    Task ConnectAndStreamAsync(
        IEnumerable<string> symbols,
        Func<RawTickV1, CancellationToken, Task> onTick,
        CancellationToken ct);
}
