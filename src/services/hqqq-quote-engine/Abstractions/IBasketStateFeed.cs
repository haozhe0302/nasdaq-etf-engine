using Hqqq.QuoteEngine.Models;

namespace Hqqq.QuoteEngine.Abstractions;

/// <summary>
/// Source of <see cref="ActiveBasket"/> activation events. Each emitted
/// value replaces the engine's current active basket. B2 implementation
/// is in-memory; B3 will swap in a Kafka-backed version subscribed to
/// <c>refdata.basket.active.v1</c> + reference-data lookups.
/// </summary>
public interface IBasketStateFeed
{
    IAsyncEnumerable<ActiveBasket> ConsumeAsync(CancellationToken ct);
}

/// <summary>
/// Sink counterpart of <see cref="IBasketStateFeed"/> used by test drivers
/// and the in-memory implementation.
/// </summary>
public interface IBasketStateSink
{
    bool TryPublish(ActiveBasket basket);
    ValueTask PublishAsync(ActiveBasket basket, CancellationToken ct);
}
