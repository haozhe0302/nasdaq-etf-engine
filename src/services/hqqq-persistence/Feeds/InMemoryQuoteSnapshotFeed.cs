using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Hqqq.Contracts.Events;
using Hqqq.Persistence.Abstractions;
using Hqqq.Persistence.Options;
using Microsoft.Extensions.Options;

namespace Hqqq.Persistence.Feeds;

/// <summary>
/// Bounded-channel implementation of <see cref="IQuoteSnapshotFeed"/> that
/// also serves as the <see cref="IQuoteSnapshotSink"/> the Kafka consumer
/// publishes into. Keeping a channel in the middle preserves backpressure
/// (writers wait when the worker falls behind) and lets tests drive the
/// worker without standing up a broker.
/// </summary>
public sealed class InMemoryQuoteSnapshotFeed : IQuoteSnapshotFeed, IQuoteSnapshotSink
{
    private readonly Channel<QuoteSnapshotV1> _channel;

    public InMemoryQuoteSnapshotFeed(IOptions<PersistenceOptions> options)
        : this(options.Value.SnapshotChannelCapacity) { }

    public InMemoryQuoteSnapshotFeed(int capacity)
    {
        if (capacity <= 0) capacity = 1;
        _channel = Channel.CreateBounded<QuoteSnapshotV1>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public bool TryPublish(QuoteSnapshotV1 snapshot) => _channel.Writer.TryWrite(snapshot);

    public ValueTask PublishAsync(QuoteSnapshotV1 snapshot, CancellationToken ct)
        => _channel.Writer.WriteAsync(snapshot, ct);

    public async IAsyncEnumerable<QuoteSnapshotV1> ConsumeAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        while (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (_channel.Reader.TryRead(out var snapshot))
                yield return snapshot;
        }
    }

    public void Complete() => _channel.Writer.TryComplete();
}
