using Hqqq.Contracts.Events;
using Hqqq.Persistence.Abstractions;
using Hqqq.Persistence.Options;
using Hqqq.Persistence.Persistence;
using Microsoft.Extensions.Options;

namespace Hqqq.Persistence.Workers;

/// <summary>
/// Drains the in-proc <see cref="IQuoteSnapshotFeed"/>, accumulates rows
/// into a batch, and flushes to Timescale via <see cref="IQuoteSnapshotWriter"/>.
/// Flushing triggers when either the batch reaches
/// <see cref="PersistenceOptions.SnapshotWriteBatchSize"/> or the
/// <see cref="PersistenceOptions.SnapshotFlushInterval"/> elapses, whichever
/// comes first.
/// </summary>
/// <remarks>
/// Writer failures are logged at error and the failing batch is retained
/// (<see cref="ConsecutiveFailureCount"/> increments) so the next loop
/// iteration retries it rather than losing it. No silent swallowing: tests
/// and metrics observe the failure counter.
/// </remarks>
public sealed class QuoteSnapshotPersistenceWorker : BackgroundService
{
    private readonly IQuoteSnapshotFeed _feed;
    private readonly IQuoteSnapshotWriter _writer;
    private readonly PersistenceOptions _options;
    private readonly ILogger<QuoteSnapshotPersistenceWorker> _logger;

    private long _consecutiveFailureCount;
    private long _totalFailureCount;
    private long _totalRowsPersisted;

    public QuoteSnapshotPersistenceWorker(
        IQuoteSnapshotFeed feed,
        IQuoteSnapshotWriter writer,
        IOptions<PersistenceOptions> options,
        ILogger<QuoteSnapshotPersistenceWorker> logger)
    {
        _feed = feed;
        _writer = writer;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Cumulative count of write failures since process start.</summary>
    public long TotalFailureCount => Interlocked.Read(ref _totalFailureCount);

    /// <summary>Number of consecutive failed flushes; resets to 0 on success.</summary>
    public long ConsecutiveFailureCount => Interlocked.Read(ref _consecutiveFailureCount);

    /// <summary>Cumulative count of rows successfully persisted since process start.</summary>
    public long TotalRowsPersisted => Interlocked.Read(ref _totalRowsPersisted);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
        => RunAsync(stoppingToken);

    /// <summary>
    /// Testable entry point equivalent to <see cref="ExecuteAsync"/>. Tests
    /// drive the feed directly and cancel <paramref name="ct"/> to end the
    /// loop without standing up a host.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        _logger.LogInformation(
            "QuoteSnapshotPersistenceWorker starting (batchSize={BatchSize}, flushInterval={FlushMs}ms)",
            _options.SnapshotWriteBatchSize,
            _options.SnapshotFlushInterval.TotalMilliseconds);

        var batch = new List<QuoteSnapshotRow>(capacity: Math.Max(1, _options.SnapshotWriteBatchSize));
        var lastFlushAt = DateTimeOffset.UtcNow;

        try
        {
            await foreach (var snapshot in _feed.ConsumeAsync(ct).ConfigureAwait(false))
            {
                QuoteSnapshotRow row;
                try
                {
                    row = QuoteSnapshotRowMapper.Map(snapshot);
                }
                catch (Exception ex)
                {
                    // Mapper only throws on structurally-bad events; the
                    // consumer already validates basic shape so this is a
                    // last-line guard rather than a common path.
                    _logger.LogWarning(ex,
                        "Dropping malformed QuoteSnapshotV1 for basket {BasketId}",
                        snapshot?.BasketId);
                    continue;
                }

                batch.Add(row);

                if (ShouldFlush(batch, lastFlushAt))
                {
                    await FlushAsync(batch, ct).ConfigureAwait(false);
                    lastFlushAt = DateTimeOffset.UtcNow;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        finally
        {
            if (batch.Count > 0)
            {
                try
                {
                    await FlushAsync(batch, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Final flush of {Count} snapshot rows on shutdown failed",
                        batch.Count);
                }
            }

            _logger.LogInformation(
                "QuoteSnapshotPersistenceWorker stopping (persisted={Persisted}, failures={Failures})",
                TotalRowsPersisted, TotalFailureCount);
        }
    }

    private bool ShouldFlush(List<QuoteSnapshotRow> batch, DateTimeOffset lastFlushAt)
    {
        if (batch.Count == 0) return false;
        if (batch.Count >= _options.SnapshotWriteBatchSize) return true;
        if (DateTimeOffset.UtcNow - lastFlushAt >= _options.SnapshotFlushInterval) return true;
        return false;
    }

    private async Task FlushAsync(List<QuoteSnapshotRow> batch, CancellationToken ct)
    {
        if (batch.Count == 0) return;

        try
        {
            await _writer.WriteBatchAsync(batch, ct).ConfigureAwait(false);

            Interlocked.Add(ref _totalRowsPersisted, batch.Count);
            Interlocked.Exchange(ref _consecutiveFailureCount, 0);
            batch.Clear();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutdown path — keep the batch so the finally-block can attempt
            // a best-effort flush against CancellationToken.None.
            throw;
        }
        catch (Exception ex)
        {
            var consecutive = Interlocked.Increment(ref _consecutiveFailureCount);
            Interlocked.Increment(ref _totalFailureCount);

            _logger.LogError(ex,
                "Quote snapshot write failed (batchSize={BatchSize}, consecutiveFailures={Consecutive}) — batch retained for retry",
                batch.Count, consecutive);

            // Retain batch — next snapshot arrival or next interval tick will
            // trigger another flush attempt. This is the explicit
            // "do not silently swallow repeated write failures" behavior.
        }
    }
}
