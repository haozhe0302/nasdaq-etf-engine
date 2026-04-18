using Hqqq.QuoteEngine.Models;

namespace Hqqq.QuoteEngine.Abstractions;

/// <summary>
/// Source of normalized ticks consumed by the quote engine.
/// B2 implementation is in-memory; B3 will swap in a Kafka-backed version
/// subscribed to <c>market.raw_ticks.v1</c>.
/// </summary>
public interface IRawTickFeed
{
    /// <summary>
    /// Long-running async stream of normalized ticks. The implementation
    /// owns cancellation semantics — the worker simply awaits foreach.
    /// </summary>
    IAsyncEnumerable<NormalizedTick> ConsumeAsync(CancellationToken ct);
}

/// <summary>
/// Sink counterpart of <see cref="IRawTickFeed"/> used by test drivers and
/// the optional in-memory implementation. Keeping it as a separate interface
/// means the production Kafka implementation does not have to expose a
/// writable surface.
/// </summary>
public interface IRawTickSink
{
    bool TryPublish(NormalizedTick tick);
    ValueTask PublishAsync(NormalizedTick tick, CancellationToken ct);
}
