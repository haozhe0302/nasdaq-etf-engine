using Hqqq.Contracts.Events;

namespace Hqqq.Ingress.Clients;

/// <summary>
/// Stub — returns an empty list. Will be replaced by a real Tiingo REST client in a later phase.
/// </summary>
public sealed class StubTiingoSnapshotClient(ILogger<StubTiingoSnapshotClient> logger) : ITiingoSnapshotClient
{
    public Task<IReadOnlyList<RawTickV1>> FetchSnapshotsAsync(IEnumerable<string> symbols, CancellationToken ct)
    {
        logger.LogInformation("StubTiingoSnapshotClient: REST snapshot fetch not yet implemented (Phase 2B)");
        return Task.FromResult<IReadOnlyList<RawTickV1>>(Array.Empty<RawTickV1>());
    }
}
