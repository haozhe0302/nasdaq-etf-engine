namespace Hqqq.Persistence.Persistence;

/// <summary>
/// Persistence seam for a batch of <see cref="QuoteSnapshotRow"/>s. The
/// worker batches on size + time and calls this once per flush. The
/// production implementation is <see cref="TimescaleQuoteSnapshotWriter"/>;
/// tests substitute an in-memory recorder.
/// </summary>
public interface IQuoteSnapshotWriter
{
    /// <summary>
    /// Writes the rows atomically. Implementations must be idempotent: a
    /// replay of the same <c>(basketId, ts)</c> must not duplicate rows.
    /// </summary>
    Task WriteBatchAsync(IReadOnlyList<QuoteSnapshotRow> rows, CancellationToken ct);
}
