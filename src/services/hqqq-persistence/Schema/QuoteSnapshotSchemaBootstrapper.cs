using Npgsql;

namespace Hqqq.Persistence.Schema;

/// <summary>
/// Ensures the <c>quote_snapshots</c> hypertable and its read-side index
/// exist. Intended to run once at service startup via
/// <see cref="Workers.SchemaBootstrapHostedService"/>. All DDL is
/// idempotent (<c>IF NOT EXISTS</c> / <c>if_not_exists => TRUE</c>) so
/// repeated invocations are safe.
/// </summary>
public sealed class QuoteSnapshotSchemaBootstrapper
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<QuoteSnapshotSchemaBootstrapper> _logger;

    public QuoteSnapshotSchemaBootstrapper(
        NpgsqlDataSource dataSource,
        ILogger<QuoteSnapshotSchemaBootstrapper> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task EnsureAsync(CancellationToken ct)
    {
        _logger.LogInformation(
            "Ensuring quote_snapshots schema ({Count} statements)",
            QuoteSnapshotSchemaSql.Statements.Count);

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);

        foreach (var sql in QuoteSnapshotSchemaSql.Statements)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        _logger.LogInformation("quote_snapshots schema is ready");
    }
}
