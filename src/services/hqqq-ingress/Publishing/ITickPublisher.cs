using Hqqq.Contracts.Events;

namespace Hqqq.Ingress.Publishing;

/// <summary>
/// Publishes normalized ticks to the downstream Kafka event bus. Each
/// call fans the tick out to both the time-series topic
/// (<c>market.raw_ticks.v1</c>) and the compacted latest-by-symbol topic
/// (<c>market.latest_by_symbol.v1</c>) so cold consumers can rehydrate
/// the latest quote without replaying the full history.
/// </summary>
public interface ITickPublisher
{
    /// <summary>
    /// Produces both the raw tick and the derived latest-symbol quote.
    /// The two produces are issued concurrently and awaited together so
    /// transient broker errors propagate to the caller.
    /// </summary>
    Task PublishAsync(RawTickV1 tick, CancellationToken ct);

    /// <summary>
    /// Convenience batch path used by the REST snapshot warmup. Default
    /// implementation just iterates <see cref="PublishAsync"/>.
    /// </summary>
    Task PublishBatchAsync(IEnumerable<RawTickV1> ticks, CancellationToken ct);
}
