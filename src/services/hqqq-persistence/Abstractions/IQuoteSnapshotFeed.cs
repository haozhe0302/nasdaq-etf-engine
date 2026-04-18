using Hqqq.Contracts.Events;

namespace Hqqq.Persistence.Abstractions;

/// <summary>
/// Source of <see cref="QuoteSnapshotV1"/> events consumed by the persistence
/// worker. The in-proc channel implementation decouples Kafka consumption
/// from Timescale writes so backpressure is explicit and the worker stays
/// testable without a live broker.
/// </summary>
public interface IQuoteSnapshotFeed
{
    /// <summary>
    /// Long-running async stream of validated snapshots. The implementation
    /// owns cancellation semantics — the worker simply awaits foreach.
    /// </summary>
    IAsyncEnumerable<QuoteSnapshotV1> ConsumeAsync(CancellationToken ct);
}
