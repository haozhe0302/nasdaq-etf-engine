using Hqqq.Contracts.Events;

namespace Hqqq.Persistence.Abstractions;

/// <summary>
/// Source of <see cref="RawTickV1"/> events consumed by the raw-tick
/// persistence worker. The in-proc channel implementation decouples Kafka
/// consumption from Timescale writes so backpressure is explicit and the
/// worker stays testable without a live broker.
/// </summary>
public interface IRawTickFeed
{
    /// <summary>
    /// Long-running async stream of validated raw ticks. The implementation
    /// owns cancellation semantics — the worker simply awaits foreach.
    /// </summary>
    IAsyncEnumerable<RawTickV1> ConsumeAsync(CancellationToken ct);
}
