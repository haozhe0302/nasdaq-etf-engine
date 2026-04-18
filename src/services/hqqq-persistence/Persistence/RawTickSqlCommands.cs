using Npgsql;
using NpgsqlTypes;

namespace Hqqq.Persistence.Persistence;

/// <summary>
/// Canonical INSERT SQL and parameter binding for the <c>raw_ticks</c>
/// hypertable. Exposed as a class of constants + static helpers so tests
/// can assert the exact idempotency clause and parameter shape without
/// running a database.
/// </summary>
public static class RawTickSqlCommands
{
    /// <summary>
    /// Ordered list of column names written by <see cref="InsertSql"/>.
    /// Excludes <c>inserted_at_utc</c>, which is populated by the table
    /// DEFAULT on first insert and preserved by the <c>DO NOTHING</c>
    /// conflict target on replay.
    /// </summary>
    public static readonly IReadOnlyList<string> InsertColumns = new[]
    {
        "symbol",
        "provider_timestamp",
        "ingress_timestamp",
        "last",
        "bid",
        "ask",
        "currency",
        "provider",
        "sequence",
    };

    /// <summary>
    /// Single-row parameterized INSERT with replay-safe conflict handling.
    /// The conflict target matches the
    /// <c>UNIQUE (symbol, provider_timestamp, sequence)</c> constraint
    /// declared by the schema bootstrapper. <c>DO NOTHING</c> means
    /// <c>inserted_at_utc</c> reflects the time of the first write.
    /// </summary>
    public const string InsertSql = """
        INSERT INTO raw_ticks (
            symbol,
            provider_timestamp,
            ingress_timestamp,
            last,
            bid,
            ask,
            currency,
            provider,
            sequence
        )
        VALUES (
            @symbol,
            @provider_timestamp,
            @ingress_timestamp,
            @last,
            @bid,
            @ask,
            @currency,
            @provider,
            @sequence
        )
        ON CONFLICT (symbol, provider_timestamp, sequence) DO NOTHING;
        """;

    /// <summary>
    /// Binds a single <see cref="RawTickRow"/> onto an existing
    /// <see cref="NpgsqlCommand"/> (whose <c>CommandText</c> is
    /// <see cref="InsertSql"/>). Extracted so callers can test parameter
    /// binding independently of a live connection.
    /// </summary>
    public static void BindRow(NpgsqlCommand command, RawTickRow row)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(row);

        command.Parameters.Clear();
        command.Parameters.Add(new NpgsqlParameter("symbol", NpgsqlDbType.Text) { Value = row.Symbol });
        command.Parameters.Add(new NpgsqlParameter("provider_timestamp", NpgsqlDbType.TimestampTz) { Value = row.ProviderTimestamp.UtcDateTime });
        command.Parameters.Add(new NpgsqlParameter("ingress_timestamp", NpgsqlDbType.TimestampTz) { Value = row.IngressTimestamp.UtcDateTime });
        command.Parameters.Add(new NpgsqlParameter("last", NpgsqlDbType.Numeric) { Value = row.Last });
        command.Parameters.Add(new NpgsqlParameter("bid", NpgsqlDbType.Numeric) { Value = (object?)row.Bid ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("ask", NpgsqlDbType.Numeric) { Value = (object?)row.Ask ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("currency", NpgsqlDbType.Text) { Value = row.Currency });
        command.Parameters.Add(new NpgsqlParameter("provider", NpgsqlDbType.Text) { Value = row.Provider });
        command.Parameters.Add(new NpgsqlParameter("sequence", NpgsqlDbType.Bigint) { Value = row.Sequence });
    }
}
