using System.Collections.Concurrent;
using Hqqq.QuoteEngine.Abstractions;
using Hqqq.QuoteEngine.Models;

namespace Hqqq.QuoteEngine.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IBasketStateSink"/> that records every basket
/// published to it — simpler than the bounded channel variant when we just
/// want to assert what the consumer forwarded.
/// </summary>
public sealed class RecordingBasketStateSink : IBasketStateSink
{
    private readonly ConcurrentQueue<ActiveBasket> _baskets = new();

    public IReadOnlyCollection<ActiveBasket> Published => _baskets;

    public bool TryPublish(ActiveBasket basket)
    {
        _baskets.Enqueue(basket);
        return true;
    }

    public ValueTask PublishAsync(ActiveBasket basket, CancellationToken ct)
    {
        _baskets.Enqueue(basket);
        return ValueTask.CompletedTask;
    }
}
