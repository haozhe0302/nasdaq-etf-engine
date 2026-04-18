using Npgsql;
using NpgsqlTypes;

namespace Hqqq.Persistence.Persistence;

/// <summary>
/// Canonical INSERT SQL and parameter binding for the
/// <c>quote_snapshots</c> hypertable. Exposed as a class of constants +
/// static helpers so tests can assert the exact idempotency clause and
/// parameter shape without running a database.
/// </summary>
public static class QuoteSnapshotSqlCommands
{
    /// <summary>
    /// Ordered list of column names written by <see cref="InsertSql"/>.
    /// Excludes <c>inserted_at_utc</c>, which is populated by the table
    /// DEFAULT on first insert and preserved by the <c>DO NOTHING</c>
    /// conflict target on replay.
    /// </summary>
    public static readonly IReadOnlyList<string> InsertColumns = new[]
    {
        "basket_id",
        "ts",
        "nav",
        "market_proxy_price",
        "premium_discount_pct",
        "stale_count",
        "fresh_count",
        "max_component_age_ms",
        "quote_quality",
    };

    /// <summary>
    /// Single-row parameterized INSERT with replay-safe conflict handling.
    /// The conflict target matches the <c>UNIQUE (basket_id, ts)</c>
    /// constraint declared by the schema bootstrapper. <c>DO NOTHING</c>
    /// means <c>inserted_at_utc</c> reflects the time of the first write.
    /// </summary>
    public const string InsertSql = """
        INSERT INTO quote_snapshots (
            basket_id,
            ts,
            nav,
            market_proxy_price,
            premium_discount_pct,
            stale_count,
            fresh_count,
            max_component_age_ms,
            quote_quality
        )
        VALUES (
            @basket_id,
            @ts,
            @nav,
            @market_proxy_price,
            @premium_discount_pct,
            @stale_count,
            @fresh_count,
            @max_component_age_ms,
            @quote_quality
        )
        ON CONFLICT (basket_id, ts) DO NOTHING;
        """;

    /// <summary>
    /// Binds a single <see cref="QuoteSnapshotRow"/> onto an existing
    /// <see cref="NpgsqlCommand"/> (whose <c>CommandText</c> is
    /// <see cref="InsertSql"/>). Extracted so callers can test parameter
    /// binding independently of a live connection.
    /// </summary>
    public static void BindRow(NpgsqlCommand command, QuoteSnapshotRow row)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(row);

        command.Parameters.Clear();
        command.Parameters.Add(new NpgsqlParameter("basket_id", NpgsqlDbType.Text) { Value = row.BasketId });
        command.Parameters.Add(new NpgsqlParameter("ts", NpgsqlDbType.TimestampTz) { Value = row.Ts.UtcDateTime });
        command.Parameters.Add(new NpgsqlParameter("nav", NpgsqlDbType.Numeric) { Value = row.Nav });
        command.Parameters.Add(new NpgsqlParameter("market_proxy_price", NpgsqlDbType.Numeric) { Value = row.MarketProxyPrice });
        command.Parameters.Add(new NpgsqlParameter("premium_discount_pct", NpgsqlDbType.Numeric) { Value = row.PremiumDiscountPct });
        command.Parameters.Add(new NpgsqlParameter("stale_count", NpgsqlDbType.Integer) { Value = row.StaleCount });
        command.Parameters.Add(new NpgsqlParameter("fresh_count", NpgsqlDbType.Integer) { Value = row.FreshCount });
        command.Parameters.Add(new NpgsqlParameter("max_component_age_ms", NpgsqlDbType.Double) { Value = row.MaxComponentAgeMs });
        command.Parameters.Add(new NpgsqlParameter("quote_quality", NpgsqlDbType.Text) { Value = row.QuoteQuality });
    }
}
