namespace Hqqq.Analytics.Timescale;

/// <summary>
/// Cheap, optional read-side seam over the <c>raw_ticks</c> hypertable. C4
/// uses this purely to attach a <em>tick count</em> to a report when the
/// operator opts in via <c>Analytics:IncludeRawTickAggregates</c>; richer
/// tick-level analytics (replay, per-symbol stats, anomaly detection) plug
/// in behind the same interface in later phases without touching the
/// report job.
/// </summary>
public interface IRawTickAggregateReader
{
    /// <summary>
    /// Returns the number of raw ticks whose <c>provider_timestamp</c>
    /// falls within the requested window across all symbols. Implementations
    /// must be side-effect-free and must not scan the table unboundedly —
    /// a single indexed <c>count(*)</c> is the intended shape.
    /// </summary>
    Task<long> CountAsync(
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken ct);
}
