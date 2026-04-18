using System.Collections.Concurrent;
using Hqqq.Contracts.Events;
using Hqqq.Persistence.Abstractions;

namespace Hqqq.Persistence.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IQuoteSnapshotSink"/> that records every accepted
/// snapshot so consumer tests can assert exact forwarding without needing
/// a live broker or a real bounded channel.
/// </summary>
public sealed class RecordingQuoteSnapshotSink : IQuoteSnapshotSink
{
    private readonly ConcurrentQueue<QuoteSnapshotV1> _published = new();

    public IReadOnlyCollection<QuoteSnapshotV1> Published => _published;

    public ValueTask PublishAsync(QuoteSnapshotV1 snapshot, CancellationToken ct)
    {
        _published.Enqueue(snapshot);
        return ValueTask.CompletedTask;
    }

    public bool TryPublish(QuoteSnapshotV1 snapshot)
    {
        _published.Enqueue(snapshot);
        return true;
    }
}
