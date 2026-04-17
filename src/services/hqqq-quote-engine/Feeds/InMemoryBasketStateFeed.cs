using System.Threading.Channels;
using Hqqq.QuoteEngine.Abstractions;
using Hqqq.QuoteEngine.Models;

namespace Hqqq.QuoteEngine.Feeds;

/// <summary>
/// Channel-backed in-memory <see cref="IBasketStateFeed"/>. Default B2
/// implementation; replaced by a Kafka consumer in B3.
/// </summary>
public sealed class InMemoryBasketStateFeed : IBasketStateFeed, IBasketStateSink
{
    private readonly Channel<ActiveBasket> _channel;

    public InMemoryBasketStateFeed() : this(capacity: 32) { }

    public InMemoryBasketStateFeed(int capacity)
    {
        _channel = Channel.CreateBounded<ActiveBasket>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    public bool TryPublish(ActiveBasket basket) => _channel.Writer.TryWrite(basket);

    public ValueTask PublishAsync(ActiveBasket basket, CancellationToken ct)
        => _channel.Writer.WriteAsync(basket, ct);

    public async IAsyncEnumerable<ActiveBasket> ConsumeAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        while (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (_channel.Reader.TryRead(out var basket))
                yield return basket;
        }
    }

    public void Complete() => _channel.Writer.TryComplete();
}
