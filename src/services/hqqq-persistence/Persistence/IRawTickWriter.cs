namespace Hqqq.Persistence.Persistence;

/// <summary>
/// Persistence seam for a batch of <see cref="RawTickRow"/>s. The raw-tick
/// worker batches on size + time and calls this once per flush. The
/// production implementation is <see cref="TimescaleRawTickWriter"/>; tests
/// substitute an in-memory recorder. Kept separate from
/// <see cref="IQuoteSnapshotWriter"/> so raw-tick and snapshot failures
/// are fully isolated.
/// </summary>
public interface IRawTickWriter
{
    /// <summary>
    /// Writes the rows atomically. Implementations must be idempotent: a
    /// replay of the same <c>(symbol, provider_timestamp, sequence)</c>
    /// must not duplicate rows.
    /// </summary>
    Task WriteBatchAsync(IReadOnlyList<RawTickRow> rows, CancellationToken ct);
}
