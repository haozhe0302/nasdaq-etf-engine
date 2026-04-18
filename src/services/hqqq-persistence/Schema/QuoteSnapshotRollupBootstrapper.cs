using Npgsql;

namespace Hqqq.Persistence.Schema;

/// <summary>
/// Ensures the continuous-aggregate rollups on top of
/// <c>quote_snapshots</c> (<c>quote_snapshots_1m</c>,
/// <c>quote_snapshots_5m</c>) and their background refresh policies
/// exist. Runs after <see cref="QuoteSnapshotSchemaBootstrapper"/> so the
/// source hypertable is guaranteed to be in place.
/// </summary>
public sealed class QuoteSnapshotRollupBootstrapper
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<QuoteSnapshotRollupBootstrapper> _logger;

    public QuoteSnapshotRollupBootstrapper(
        NpgsqlDataSource dataSource,
        ILogger<QuoteSnapshotRollupBootstrapper> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task EnsureAsync(CancellationToken ct)
    {
        _logger.LogInformation(
            "Ensuring quote_snapshots rollups ({Count} statements)",
            QuoteSnapshotRollupSchemaSql.Statements.Count);

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);

        foreach (var sql in QuoteSnapshotRollupSchemaSql.Statements)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        _logger.LogInformation("quote_snapshots rollups are ready");
    }
}
