using System.Collections.Concurrent;
using Hqqq.Contracts.Events;
using Hqqq.Persistence.Abstractions;

namespace Hqqq.Persistence.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IRawTickSink"/> that records every accepted raw
/// tick so consumer tests can assert exact forwarding without needing a
/// live broker or a real bounded channel.
/// </summary>
public sealed class RecordingRawTickSink : IRawTickSink
{
    private readonly ConcurrentQueue<RawTickV1> _published = new();

    public IReadOnlyCollection<RawTickV1> Published => _published;

    public ValueTask PublishAsync(RawTickV1 tick, CancellationToken ct)
    {
        _published.Enqueue(tick);
        return ValueTask.CompletedTask;
    }

    public bool TryPublish(RawTickV1 tick)
    {
        _published.Enqueue(tick);
        return true;
    }
}
