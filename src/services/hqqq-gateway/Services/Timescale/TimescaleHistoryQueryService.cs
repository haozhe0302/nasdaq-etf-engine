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
    internal const string SelectHistorySql = """
        SELECT ts, nav, market_proxy_price
        FROM quote_snapshots
        WHERE basket_id = @basket_id
          AND ts >= @from_utc
          AND ts <= @to_utc
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
