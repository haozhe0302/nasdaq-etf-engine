using Npgsql;
using NpgsqlTypes;

namespace Hqqq.Analytics.Timescale;

/// <summary>
/// Single-query raw-tick aggregate reader. Intentionally narrow for C4: a
/// single <c>count(*)</c> served by the ascending-time index on
/// <c>raw_ticks(provider_timestamp)</c>.
/// </summary>
public sealed class TimescaleRawTickAggregateReader : IRawTickAggregateReader
{
    internal const string CountSql = """
        SELECT count(*)
        FROM raw_ticks
        WHERE provider_timestamp >= @from_utc
          AND provider_timestamp <= @to_utc;
        """;

    private readonly NpgsqlDataSource _dataSource;

    public TimescaleRawTickAggregateReader(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    public async Task<long> CountAsync(DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(CountSql);
        command.Parameters.Add(new NpgsqlParameter("from_utc", NpgsqlDbType.TimestampTz) { Value = startUtc.UtcDateTime });
        command.Parameters.Add(new NpgsqlParameter("to_utc", NpgsqlDbType.TimestampTz) { Value = endUtc.UtcDateTime });

        var scalar = await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return scalar is long l ? l : Convert.ToInt64(scalar, System.Globalization.CultureInfo.InvariantCulture);
    }
}
