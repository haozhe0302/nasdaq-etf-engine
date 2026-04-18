using Hqqq.Contracts.Dtos;

namespace Hqqq.QuoteEngine.Abstractions;

/// <summary>
/// Sink for the latest materialized quote snapshot. Writes the current
/// basket's serving-shape <see cref="QuoteSnapshotDto"/> to a latest-state
/// store (Redis in production). Intentionally scoped to overwrite semantics —
/// this is not an event stream; only the most recent snapshot per basket is
/// retained.
/// </summary>
public interface IQuoteSnapshotSink
{
    Task WriteAsync(string basketId, QuoteSnapshotDto snapshot, CancellationToken ct);
}
