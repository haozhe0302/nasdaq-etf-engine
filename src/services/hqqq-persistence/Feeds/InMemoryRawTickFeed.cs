using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Hqqq.Contracts.Events;
using Hqqq.Persistence.Abstractions;
using Hqqq.Persistence.Options;
using Microsoft.Extensions.Options;

namespace Hqqq.Persistence.Feeds;

/// <summary>
/// Bounded-channel implementation of <see cref="IRawTickFeed"/> that also
/// serves as the <see cref="IRawTickSink"/> the Kafka consumer publishes
/// into. Keeping a channel in the middle preserves backpressure (writers
/// wait when the worker falls behind) and lets tests drive the worker
/// without standing up a broker. Independent from
/// <see cref="InMemoryQuoteSnapshotFeed"/> so raw-tick and snapshot
/// pipelines do not share buffers or failure modes.
/// </summary>
public sealed class InMemoryRawTickFeed : IRawTickFeed, IRawTickSink
{
    private readonly Channel<RawTickV1> _channel;

    public InMemoryRawTickFeed(IOptions<PersistenceOptions> options)
        : this(options.Value.RawTickChannelCapacity) { }

    public InMemoryRawTickFeed(int capacity)
    {
        if (capacity <= 0) capacity = 1;
        _channel = Channel.CreateBounded<RawTickV1>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public bool TryPublish(RawTickV1 tick) => _channel.Writer.TryWrite(tick);

    public ValueTask PublishAsync(RawTickV1 tick, CancellationToken ct)
        => _channel.Writer.WriteAsync(tick, ct);

    public async IAsyncEnumerable<RawTickV1> ConsumeAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        while (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (_channel.Reader.TryRead(out var tick))
                yield return tick;
        }
    }

    public void Complete() => _channel.Writer.TryComplete();
}
