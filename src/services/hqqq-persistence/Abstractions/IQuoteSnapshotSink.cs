using Hqqq.Contracts.Events;

namespace Hqqq.Persistence.Abstractions;

/// <summary>
/// Sink counterpart of <see cref="IQuoteSnapshotFeed"/>. The Kafka consumer
/// publishes validated snapshots here; test drivers push directly to exercise
/// the worker pipeline without a broker.
/// </summary>
public interface IQuoteSnapshotSink
{
    bool TryPublish(QuoteSnapshotV1 snapshot);
    ValueTask PublishAsync(QuoteSnapshotV1 snapshot, CancellationToken ct);
}
