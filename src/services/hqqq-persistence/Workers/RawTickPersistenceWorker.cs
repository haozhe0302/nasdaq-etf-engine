using Hqqq.Contracts.Events;
using Hqqq.Persistence.Abstractions;
using Hqqq.Persistence.Options;
using Hqqq.Persistence.Persistence;
using Microsoft.Extensions.Options;

namespace Hqqq.Persistence.Workers;

/// <summary>
/// Drains the in-proc <see cref="IRawTickFeed"/>, accumulates rows into a
/// batch, and flushes to Timescale via <see cref="IRawTickWriter"/>.
/// Flushing triggers when either the batch reaches
/// <see cref="PersistenceOptions.RawTickWriteBatchSize"/> or
/// <see cref="PersistenceOptions.RawTickFlushInterval"/> elapses, whichever
/// comes first.
/// </summary>
/// <remarks>
/// Writer failures are logged at error and the failing batch is retained
/// (<see cref="ConsecutiveFailureCount"/> increments) so the next loop
/// iteration retries it rather than losing it. Failure counters are
/// independent from <see cref="QuoteSnapshotPersistenceWorker"/> so raw
/// tick and snapshot pipeline health can be observed separately.
/// </remarks>
public sealed class RawTickPersistenceWorker : BackgroundService
{
    private readonly IRawTickFeed _feed;
    private readonly IRawTickWriter _writer;
    private readonly PersistenceOptions _options;
    private readonly ILogger<RawTickPersistenceWorker> _logger;

    private long _consecutiveFailureCount;
    private long _totalFailureCount;
    private long _totalRowsPersisted;

    public RawTickPersistenceWorker(
        IRawTickFeed feed,
        IRawTickWriter writer,
        IOptions<PersistenceOptions> options,
        ILogger<RawTickPersistenceWorker> logger)
    {
        _feed = feed;
        _writer = writer;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Cumulative count of raw-tick write failures since process start.</summary>
    public long TotalFailureCount => Interlocked.Read(ref _totalFailureCount);

    /// <summary>Number of consecutive failed flushes; resets to 0 on success.</summary>
    public long ConsecutiveFailureCount => Interlocked.Read(ref _consecutiveFailureCount);

    /// <summary>Cumulative count of raw-tick rows successfully persisted since process start.</summary>
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
            "RawTickPersistenceWorker starting (batchSize={BatchSize}, flushInterval={FlushMs}ms)",
            _options.RawTickWriteBatchSize,
            _options.RawTickFlushInterval.TotalMilliseconds);

        var batch = new List<RawTickRow>(capacity: Math.Max(1, _options.RawTickWriteBatchSize));
        var lastFlushAt = DateTimeOffset.UtcNow;

        try
        {
            await foreach (var tick in _feed.ConsumeAsync(ct).ConfigureAwait(false))
            {
                RawTickRow row;
                try
                {
                    row = RawTickRowMapper.Map(tick);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Dropping malformed RawTickV1 for symbol {Symbol}",
                        tick?.Symbol);
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
                        "Final flush of {Count} raw tick rows on shutdown failed",
                        batch.Count);
                }
            }

            _logger.LogInformation(
                "RawTickPersistenceWorker stopping (persisted={Persisted}, failures={Failures})",
                TotalRowsPersisted, TotalFailureCount);
        }
    }

    private bool ShouldFlush(List<RawTickRow> batch, DateTimeOffset lastFlushAt)
    {
        if (batch.Count == 0) return false;
        if (batch.Count >= _options.RawTickWriteBatchSize) return true;
        if (DateTimeOffset.UtcNow - lastFlushAt >= _options.RawTickFlushInterval) return true;
        return false;
    }

    private async Task FlushAsync(List<RawTickRow> batch, CancellationToken ct)
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
                "Raw tick write failed (batchSize={BatchSize}, consecutiveFailures={Consecutive}) — batch retained for retry",
                batch.Count, consecutive);
        }
    }
}
