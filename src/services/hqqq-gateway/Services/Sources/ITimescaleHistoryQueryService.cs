using Hqqq.Gateway.Services.Timescale;

namespace Hqqq.Gateway.Services.Sources;

/// <summary>
/// Repository seam that isolates Timescale SQL from the history source
/// composition logic. Production implementation hits
/// <c>quote_snapshots</c> via <see cref="Npgsql.NpgsqlDataSource"/>;
/// tests substitute an in-memory fake.
/// </summary>
public interface ITimescaleHistoryQueryService
{
    /// <summary>
    /// Loads quote snapshots for <paramref name="basketId"/> whose
    /// <c>ts</c> falls in the closed interval
    /// <c>[<paramref name="fromUtc"/>, <paramref name="toUtc"/>]</c>, ordered
    /// ascending by <c>ts</c>.
    /// </summary>
    Task<IReadOnlyList<HistoryRow>> LoadAsync(
        string basketId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct);
}
