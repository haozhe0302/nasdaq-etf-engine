namespace Hqqq.Ingress.Clients;

/// <summary>
/// Abstraction over the Tiingo WebSocket streaming connection.
/// </summary>
public interface ITiingoStreamClient
{
    Task ConnectAndStreamAsync(IEnumerable<string> symbols, CancellationToken ct);
    bool IsConnected { get; }
    DateTimeOffset? LastDataUtc { get; }
}
