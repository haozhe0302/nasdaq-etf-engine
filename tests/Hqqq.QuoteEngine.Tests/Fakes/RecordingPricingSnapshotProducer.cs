using System.Collections.Concurrent;
using Hqqq.Contracts.Events;
using Hqqq.QuoteEngine.Publishing;

namespace Hqqq.QuoteEngine.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IPricingSnapshotProducer"/> that records every
/// (topic, key, value) publish so tests can assert exact routing without
/// running a broker.
/// </summary>
public sealed class RecordingPricingSnapshotProducer : IPricingSnapshotProducer
{
    private readonly ConcurrentQueue<(string Topic, string Key, QuoteSnapshotV1 Value)> _published = new();

    public IReadOnlyCollection<(string Topic, string Key, QuoteSnapshotV1 Value)> Published => _published;

    public Task PublishAsync(string topic, string key, QuoteSnapshotV1 value, CancellationToken ct)
    {
        _published.Enqueue((topic, key, value));
        return Task.CompletedTask;
    }
}
