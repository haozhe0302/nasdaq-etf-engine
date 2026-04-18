using Npgsql;

namespace Hqqq.Persistence.Persistence;

/// <summary>
/// Production <see cref="IRawTickWriter"/> that batches rows into a single
/// transactional write against the <c>raw_ticks</c> hypertable using
/// <see cref="RawTickSqlCommands.InsertSql"/> and its idempotent
/// <c>ON CONFLICT ... DO NOTHING</c> clause.
/// </summary>
/// <remarks>
/// All DB failures are logged at error and rethrown so the raw-tick worker
/// can increment its failure counter and decide whether to retry. Does
/// not share state with <see cref="TimescaleQuoteSnapshotWriter"/> — the
/// two pipelines are isolated by design.
/// </remarks>
public sealed class TimescaleRawTickWriter : IRawTickWriter
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<TimescaleRawTickWriter> _logger;

    public TimescaleRawTickWriter(
        NpgsqlDataSource dataSource,
        ILogger<TimescaleRawTickWriter> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task WriteBatchAsync(IReadOnlyList<RawTickRow> rows, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0) return;

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            await using var command = new NpgsqlCommand(RawTickSqlCommands.InsertSql, connection, transaction);

            for (var i = 0; i < rows.Count; i++)
            {
                RawTickSqlCommands.BindRow(command, rows[i]);
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);

            _logger.LogDebug(
                "Persisted {Count} raw tick rows to Timescale", rows.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to persist {Count} raw tick rows — rolling back batch",
                rows.Count);

            try
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
            }
            catch (Exception rollbackEx)
            {
                _logger.LogWarning(rollbackEx,
                    "Rollback of raw tick batch failed");
            }

            throw;
        }
    }
}
