namespace Hqqq.Ingress.Clients;

/// <summary>
/// Stub — logs that the WebSocket client is not yet implemented and awaits cancellation.
/// Will be replaced by a real Tiingo WebSocket implementation in a later phase.
/// </summary>
public sealed class StubTiingoStreamClient(ILogger<StubTiingoStreamClient> logger) : ITiingoStreamClient
{
    public bool IsConnected => false;
    public DateTimeOffset? LastDataUtc => null;

    public async Task ConnectAndStreamAsync(IEnumerable<string> symbols, CancellationToken ct)
    {
        logger.LogInformation("StubTiingoStreamClient: WebSocket streaming not yet implemented (Phase 2B)");
        await Task.Delay(Timeout.Infinite, ct);
    }
}
