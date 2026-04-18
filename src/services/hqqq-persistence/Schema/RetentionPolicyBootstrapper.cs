using Hqqq.Persistence.Options;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Hqqq.Persistence.Schema;

/// <summary>
/// Registers Timescale retention policies on the raw-tick hypertable, the
/// quote-snapshot hypertable, and the rollup continuous aggregates, using
/// windows configured in <see cref="PersistenceOptions"/>. Runs after
/// every schema/rollup has been created so each target relation already
/// exists.
/// </summary>
public sealed class RetentionPolicyBootstrapper
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly PersistenceOptions _options;
    private readonly ILogger<RetentionPolicyBootstrapper> _logger;

    public RetentionPolicyBootstrapper(
        NpgsqlDataSource dataSource,
        IOptions<PersistenceOptions> options,
        ILogger<RetentionPolicyBootstrapper> logger)
    {
        _dataSource = dataSource;
        _options = options.Value;
        _logger = logger;
    }

    public async Task EnsureAsync(CancellationToken ct)
    {
        var statements = RetentionPolicySchemaSql.BuildStatements(_options);

        _logger.LogInformation(
            "Ensuring retention policies (rawTicks={RawTicks}, quoteSnapshots={Snapshots}, rollups={Rollups})",
            _options.RawTickRetention, _options.QuoteSnapshotRetention, _options.RollupRetention);

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);

        foreach (var sql in statements)
        {
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        _logger.LogInformation("retention policies are ready");
    }
}
