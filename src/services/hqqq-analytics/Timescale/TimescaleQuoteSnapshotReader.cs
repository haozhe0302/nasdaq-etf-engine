using Npgsql;
using NpgsqlTypes;

namespace Hqqq.Analytics.Timescale;

/// <summary>
/// Production <see cref="IQuoteSnapshotReader"/> backed by a shared
/// <see cref="NpgsqlDataSource"/>. Selects the full projection written by
/// the persistence service's <c>quote_snapshots</c> INSERT so the pure
/// calculator can compute every summary metric without a second round-trip.
/// </summary>
/// <remarks>
/// Uses the basket-scoped read index <c>ix_quote_snapshots_basket_ts_desc</c>
/// installed by the persistence schema bootstrapper. Ordered ASC on
/// <c>ts</c> so downstream statistics and gap detection can walk rows in
/// chronological order. <c>LIMIT maxRows + 1</c> is used to detect overflow
/// deterministically.
/// </remarks>
public sealed class TimescaleQuoteSnapshotReader : IQuoteSnapshotReader
{
    internal const string SelectSql = """
        SELECT ts, nav, market_proxy_price, premium_discount_pct,
               stale_count, fresh_count, max_component_age_ms, quote_quality
        FROM quote_snapshots
        WHERE basket_id = @basket_id
          AND ts >= @from_utc
          AND ts <= @to_utc
        ORDER BY ts ASC
        LIMIT @row_limit;
        """;

    private readonly NpgsqlDataSource _dataSource;

    public TimescaleQuoteSnapshotReader(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async Task<IReadOnlyList<QuoteSnapshotRecord>> LoadAsync(
        string basketId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        int maxRows,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basketId);
        if (maxRows <= 0) throw new ArgumentOutOfRangeException(nameof(maxRows));

        await using var command = _dataSource.CreateCommand(SelectSql);
        command.Parameters.Add(new NpgsqlParameter("basket_id", NpgsqlDbType.Text) { Value = basketId });
        command.Parameters.Add(new NpgsqlParameter("from_utc", NpgsqlDbType.TimestampTz) { Value = startUtc.UtcDateTime });
        command.Parameters.Add(new NpgsqlParameter("to_utc", NpgsqlDbType.TimestampTz) { Value = endUtc.UtcDateTime });
        command.Parameters.Add(new NpgsqlParameter("row_limit", NpgsqlDbType.Integer) { Value = maxRows + 1 });

        var rows = new List<QuoteSnapshotRecord>(Math.Min(maxRows, 1024));

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var tsUtc = reader.GetFieldValue<DateTime>(0);
            rows.Add(new QuoteSnapshotRecord
            {
                BasketId = basketId,
                Ts = new DateTimeOffset(DateTime.SpecifyKind(tsUtc, DateTimeKind.Utc)),
                Nav = reader.GetFieldValue<decimal>(1),
                MarketProxyPrice = reader.GetFieldValue<decimal>(2),
                PremiumDiscountPct = reader.GetFieldValue<decimal>(3),
                StaleCount = reader.GetFieldValue<int>(4),
                FreshCount = reader.GetFieldValue<int>(5),
                MaxComponentAgeMs = reader.GetFieldValue<double>(6),
                QuoteQuality = reader.GetFieldValue<string>(7),
            });

            if (rows.Count > maxRows)
            {
                throw new InvalidOperationException(
                    $"Analytics read refused: window returned more than MaxRows={maxRows} rows " +
                    $"for basket '{basketId}' between {startUtc:O} and {endUtc:O}. " +
                    "Narrow the window or raise Analytics:MaxRows.");
            }
        }

        return rows;
    }
}
