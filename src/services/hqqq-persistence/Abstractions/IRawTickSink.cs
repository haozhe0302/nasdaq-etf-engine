using Hqqq.Contracts.Events;

namespace Hqqq.Persistence.Abstractions;

/// <summary>
/// Sink counterpart of <see cref="IRawTickFeed"/>. The raw-tick Kafka
/// consumer publishes validated ticks here; test drivers push directly to
/// exercise the worker pipeline without a broker.
/// </summary>
public interface IRawTickSink
{
    bool TryPublish(RawTickV1 tick);
    ValueTask PublishAsync(RawTickV1 tick, CancellationToken ct);
}
