namespace Hqqq.Analytics.Timescale;

/// <summary>
/// Read-side seam over the <c>quote_snapshots</c> hypertable. Returns fully
/// mapped <see cref="QuoteSnapshotRecord"/> instances in ascending <c>Ts</c>
/// order so the pure calculator does not have to sort.
/// </summary>
public interface IQuoteSnapshotReader
{
    /// <summary>
    /// Loads at most <paramref name="maxRows"/> rows for the requested
    /// basket/window. Implementations MUST fail fast (throw) when the
    /// database contains more than <paramref name="maxRows"/> matching
    /// rows rather than silently truncating.
    /// </summary>
    Task<IReadOnlyList<QuoteSnapshotRecord>> LoadAsync(
        string basketId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        int maxRows,
        CancellationToken ct);
}
