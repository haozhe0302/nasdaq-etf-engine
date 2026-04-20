using Hqqq.Contracts.Events;

namespace Hqqq.Ingress.Clients;

/// <summary>
/// Abstraction over the Tiingo REST snapshot endpoint (batch price
/// fetch). Used at startup to seed <c>market.latest_by_symbol.v1</c>
/// before the first websocket tick arrives, so consumers (notably
/// <c>quote-engine</c>) have a baseline price to compute against.
/// </summary>
public interface ITiingoSnapshotClient
{
    /// <summary>
    /// Fetches the latest IEX price for each symbol. Symbols whose price
    /// cannot be resolved are silently skipped; partial success is
    /// preferred over throwing because the websocket will catch up.
    /// </summary>
    Task<IReadOnlyList<RawTickV1>> FetchSnapshotsAsync(
        IEnumerable<string> symbols, CancellationToken ct);
}
