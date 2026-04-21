using System.Collections.Concurrent;
using Hqqq.Contracts.Events;
using Hqqq.ReferenceData.Publishing;

namespace Hqqq.ReferenceData.Tests.TestDoubles;

/// <summary>
/// Shared test double: in-memory <see cref="IBasketPublisher"/> that
/// records every published event and can optionally throw on publish to
/// exercise failure handling.
/// </summary>
internal sealed class CapturingPublisher : IBasketPublisher
{
    private readonly ConcurrentQueue<BasketActiveStateV1> _published = new();

    public IReadOnlyList<BasketActiveStateV1> Published => _published.ToArray();

    public Exception? ThrowOnPublish { get; set; }

    public Task PublishAsync(BasketActiveStateV1 state, CancellationToken ct)
    {
        if (ThrowOnPublish is not null) throw ThrowOnPublish;
        _published.Enqueue(state);
        return Task.CompletedTask;
    }
}
