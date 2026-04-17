using Hqqq.Contracts.Events;

namespace Hqqq.QuoteEngine.Abstractions;

/// <summary>
/// Publishes <see cref="QuoteSnapshotV1"/> events onto the system event bus
/// (Kafka topic <c>pricing.snapshots.v1</c> in production). Downstream
/// persistence, analytics, and gateway services consume these events; the
/// engine never reads back from this sink.
/// </summary>
public interface ISnapshotEventPublisher
{
    Task PublishAsync(QuoteSnapshotV1 snapshot, CancellationToken ct);
}
