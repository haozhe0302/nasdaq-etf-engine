using Hqqq.Contracts.Events;

namespace Hqqq.Ingress.Publishing;

/// <summary>
/// Publishes normalized ticks to the downstream event bus.
/// Kafka implementation replaces this stub in a later phase.
/// </summary>
public interface ITickPublisher
{
    Task PublishAsync(RawTickV1 tick, CancellationToken ct);
    Task PublishBatchAsync(IEnumerable<RawTickV1> ticks, CancellationToken ct);
}
