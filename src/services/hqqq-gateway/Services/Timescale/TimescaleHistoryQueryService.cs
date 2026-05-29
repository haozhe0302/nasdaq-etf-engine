using Hqqq.Gateway.Services.Sources;
using Npgsql;
using NpgsqlTypes;

namespace Hqqq.Gateway.Services.Timescale;

/// <summary>
/// Production <see cref="ITimescaleHistoryQueryService"/> backed by a
/// shared <see cref="NpgsqlDataSource"/>. Reads <c>quote_snapshots</c>
/// directly using the basket-scoped read index
/// (<c>ix_quote_snapshots_basket_ts_desc</c>) installed by the
/// persistence schema bootstrapper.
/// </summary>
public sealed class TimescaleHistoryQueryService : ITimescaleHistoryQueryService
{
    // ── SQL ──────────────────────────────────────────────
    // Ordered ASC so downstream stats / downsampling / gap detection can
    // walk rows in chronological order without an extra sort.
    //
    // The UTC [from, to) range is the broad bounding window (end-exclusive
    // via `ts < @to_utc` to avoid double-counting a boundary row that also
    // opens the next window). On top of that we defensively re-apply the
    // regular trading session filter (09:30–16:00 ET, Mon–Fri) so any old
    // non-RTH rows still sitting in the hypertable do not leak into
    // /api/history. Persistence already gates writes to RTH; this is a
    // cheap, idempotent safety net that matches the cleanup-script and
    // persistence-gate definition of a regular-session point.
    internal const string SelectHistorySql = """
        SELECT ts, nav, market_proxy_price
        FROM quote_snapshots
        WHERE basket_id = @basket_id
          AND ts >= @from_utc
          AND ts <  @to_utc
          AND (ts AT TIME ZONE 'America/New_York')::time >= TIME '09:30'
          AND (ts AT TIME ZONE 'America/New_York')::time <  TIME '16:00'
          AND EXTRACT(ISODOW FROM (ts AT TIME ZONE 'America/New_York')) BETWEEN 1 AND 5
        ORDER BY ts ASC;
        """;

    private readonly NpgsqlDataSource _dataSource;

    public TimescaleHistoryQueryService(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyList<HistoryRow>> LoadAsync(
        string basketId,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basketId);

        await using var command = _dataSource.CreateCommand(SelectHistorySql);
        command.Parameters.Add(new NpgsqlParameter("basket_id", NpgsqlDbType.Text) { Value = basketId });
        command.Parameters.Add(new NpgsqlParameter("from_utc", NpgsqlDbType.TimestampTz) { Value = fromUtc.UtcDateTime });
        command.Parameters.Add(new NpgsqlParameter("to_utc", NpgsqlDbType.TimestampTz) { Value = toUtc.UtcDateTime });

        var rows = new List<HistoryRow>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var tsUtc = reader.GetFieldValue<DateTime>(0);
            var nav = reader.GetFieldValue<decimal>(1);
            var marketProxyPrice = reader.GetFieldValue<decimal>(2);
            rows.Add(new HistoryRow(
                new DateTimeOffset(DateTime.SpecifyKind(tsUtc, DateTimeKind.Utc)),
                nav,
                marketProxyPrice));
        }

        return rows;
    }
}
