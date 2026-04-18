using Npgsql;

namespace Hqqq.Persistence.Schema;

/// <summary>
/// Ensures the <c>raw_ticks</c> hypertable and its read-side indexes
/// exist. Intended to run once at service startup via
/// <see cref="Workers.SchemaBootstrapHostedService"/>. All DDL is
/// idempotent (<c>IF NOT EXISTS</c> / <c>if_not_exists => TRUE</c>) so
/// repeated invocations are safe.
/// </summary>
public sealed class RawTickSchemaBootstrapper
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<RawTickSchemaBootstrapper> _logger;

    public RawTickSchemaBootstrapper(
        NpgsqlDataSource dataSource,
        ILogger<RawTickSchemaBootstrapper> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task EnsureAsync(CancellationToken ct)
    {
        _logger.LogInformation(
            "Ensuring raw_ticks schema ({Count} statements)",
            RawTickSchemaSql.Statements.Count);

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);

        foreach (var sql in RawTickSchemaSql.Statements)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        _logger.LogInformation("raw_ticks schema is ready");
    }
}
