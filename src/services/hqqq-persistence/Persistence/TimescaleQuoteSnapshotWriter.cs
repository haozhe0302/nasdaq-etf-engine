using Npgsql;

namespace Hqqq.Persistence.Persistence;

/// <summary>
/// Production <see cref="IQuoteSnapshotWriter"/> that batches rows into a
/// single transactional write against the <c>quote_snapshots</c>
/// hypertable using <see cref="QuoteSnapshotSqlCommands.InsertSql"/> and
/// its idempotent <c>ON CONFLICT ... DO NOTHING</c> clause.
/// </summary>
/// <remarks>
/// All DB failures are logged at error and rethrown so the worker can
/// increment its failure counter and decide whether to retry.
/// </remarks>
public sealed class TimescaleQuoteSnapshotWriter : IQuoteSnapshotWriter
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<TimescaleQuoteSnapshotWriter> _logger;

    public TimescaleQuoteSnapshotWriter(
        NpgsqlDataSource dataSource,
        ILogger<TimescaleQuoteSnapshotWriter> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task WriteBatchAsync(IReadOnlyList<QuoteSnapshotRow> rows, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0) return;

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        try
        {
            await using var command = new NpgsqlCommand(QuoteSnapshotSqlCommands.InsertSql, connection, transaction);

            for (var i = 0; i < rows.Count; i++)
            {
                QuoteSnapshotSqlCommands.BindRow(command, rows[i]);
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);

            _logger.LogDebug(
                "Persisted {Count} quote snapshot rows to Timescale", rows.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to persist {Count} quote snapshot rows — rolling back batch",
                rows.Count);

            try
            {
                await transaction.RollbackAsync(ct).ConfigureAwait(false);
            }
            catch (Exception rollbackEx)
            {
                _logger.LogWarning(rollbackEx,
                    "Rollback of quote snapshot batch failed");
            }

            throw;
        }
    }
}
