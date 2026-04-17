using Hqqq.Contracts.Events;

namespace Hqqq.QuoteEngine.Publishing;

/// <summary>
/// Narrow seam over the Kafka producer used by
/// <see cref="SnapshotTopicPublisher"/>. Keeping it Confluent-Kafka-agnostic
/// lets tests drive the publisher without standing up a broker while the
/// production implementation wraps a single long-lived
/// <c>IProducer&lt;string, QuoteSnapshotV1&gt;</c>.
/// </summary>
public interface IPricingSnapshotProducer
{
    Task PublishAsync(string topic, string key, QuoteSnapshotV1 value, CancellationToken ct);
}
