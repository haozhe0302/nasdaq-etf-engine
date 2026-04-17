using System.Threading.Channels;
using Hqqq.QuoteEngine.Abstractions;
using Hqqq.QuoteEngine.Models;

namespace Hqqq.QuoteEngine.Feeds;

/// <summary>
/// Channel-backed in-memory <see cref="IRawTickFeed"/>. Default B2
/// implementation; replaced by a Kafka consumer in B3. Also serves as
/// the fake used by tests.
/// </summary>
public sealed class InMemoryRawTickFeed : IRawTickFeed, IRawTickSink
{
    private readonly Channel<NormalizedTick> _channel;

    public InMemoryRawTickFeed() : this(capacity: 1024) { }

    public InMemoryRawTickFeed(int capacity)
    {
        _channel = Channel.CreateBounded<NormalizedTick>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public bool TryPublish(NormalizedTick tick) => _channel.Writer.TryWrite(tick);

    public ValueTask PublishAsync(NormalizedTick tick, CancellationToken ct)
        => _channel.Writer.WriteAsync(tick, ct);

    public async IAsyncEnumerable<NormalizedTick> ConsumeAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        while (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (_channel.Reader.TryRead(out var tick))
                yield return tick;
        }
    }

    public void Complete() => _channel.Writer.TryComplete();
}
