using Hqqq.Contracts.Events;

namespace Hqqq.Ingress.Clients;

/// <summary>
/// Stub used in <see cref="Hqqq.Infrastructure.Hosting.OperatingMode.Hybrid"/>
/// mode. Returns an empty snapshot since the legacy monolith owns
/// snapshot bridging in that posture.
/// </summary>
public sealed class StubTiingoSnapshotClient(ILogger<StubTiingoSnapshotClient> logger) : ITiingoSnapshotClient
{
    public Task<IReadOnlyList<RawTickV1>> FetchSnapshotsAsync(
        IEnumerable<string> symbols, CancellationToken ct)
    {
        logger.LogInformation(
            "StubTiingoSnapshotClient: hybrid mode — REST snapshot disabled (monolith owns warmup)");
        return Task.FromResult<IReadOnlyList<RawTickV1>>(Array.Empty<RawTickV1>());
    }
}
