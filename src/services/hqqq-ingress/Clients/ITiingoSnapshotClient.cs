using Hqqq.Contracts.Events;

namespace Hqqq.Ingress.Clients;

/// <summary>
/// Abstraction over the Tiingo REST snapshot endpoint (batch price fetch).
/// </summary>
public interface ITiingoSnapshotClient
{
    Task<IReadOnlyList<RawTickV1>> FetchSnapshotsAsync(IEnumerable<string> symbols, CancellationToken ct);
}
