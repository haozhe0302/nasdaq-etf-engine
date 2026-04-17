using System.Threading.Channels;
using Hqqq.QuoteEngine.Abstractions;
using Hqqq.QuoteEngine.Models;

namespace Hqqq.QuoteEngine.Tests.Fakes;

public sealed class FakeBasketStateFeed : IBasketStateFeed
{
    private readonly Channel<ActiveBasket> _channel =
        Channel.CreateUnbounded<ActiveBasket>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    public void Enqueue(ActiveBasket basket) => _channel.Writer.TryWrite(basket);
    public void Complete() => _channel.Writer.TryComplete();

    public async IAsyncEnumerable<ActiveBasket> ConsumeAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        while (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (_channel.Reader.TryRead(out var basket))
                yield return basket;
        }
    }
}
