using System.Collections.Concurrent;
using Hqqq.QuoteEngine.Abstractions;
using Hqqq.QuoteEngine.Models;

namespace Hqqq.QuoteEngine.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IRawTickSink"/> that records every tick forwarded
/// by the consumer so tests can assert on validate/skip semantics without
/// standing up the channel-based sink.
/// </summary>
public sealed class RecordingRawTickSink : IRawTickSink
{
    private readonly ConcurrentQueue<NormalizedTick> _ticks = new();

    public IReadOnlyCollection<NormalizedTick> Published => _ticks;

    public bool TryPublish(NormalizedTick tick)
    {
        _ticks.Enqueue(tick);
        return true;
    }

    public ValueTask PublishAsync(NormalizedTick tick, CancellationToken ct)
    {
        _ticks.Enqueue(tick);
        return ValueTask.CompletedTask;
    }
}
