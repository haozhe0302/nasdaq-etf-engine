using Hqqq.Contracts.Events;

namespace Hqqq.Ingress.Clients;

/// <summary>
/// Stub used in <see cref="Hqqq.Infrastructure.Hosting.OperatingMode.Hybrid"/>
/// mode where the legacy monolith bridges ticks. Logs once and idles
/// until cancellation. Never invokes the tick callback.
/// </summary>
public sealed class StubTiingoStreamClient(ILogger<StubTiingoStreamClient> logger) : ITiingoStreamClient
{
    public bool IsConnected => false;
    public DateTimeOffset? LastDataUtc => null;

    public async Task ConnectAndStreamAsync(
        IEnumerable<string> symbols,
        Func<RawTickV1, CancellationToken, Task> onTick,
        CancellationToken ct)
    {
        logger.LogInformation(
            "StubTiingoStreamClient: hybrid mode — Tiingo ingest disabled (monolith bridges ticks)");
        await Task.Delay(Timeout.Infinite, ct);
    }
}
