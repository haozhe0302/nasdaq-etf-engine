using System.Collections.Concurrent;
using Hqqq.Contracts.Dtos;
using Hqqq.QuoteEngine.Abstractions;

namespace Hqqq.QuoteEngine.Tests.Fakes;

/// <summary>
/// Recording <see cref="IQuoteUpdatePublisher"/> for worker integration
/// tests. Captures every published delta so tests can assert invocation
/// count, no-op suppression, and DTO shape.
/// </summary>
public sealed class RecordingQuoteUpdatePublisher : IQuoteUpdatePublisher
{
    private readonly ConcurrentQueue<(string BasketId, QuoteUpdateDto Update)> _published = new();

    public IReadOnlyCollection<(string BasketId, QuoteUpdateDto Update)> Published => _published;

    public Task PublishAsync(string basketId, QuoteUpdateDto update, CancellationToken ct)
    {
        _published.Enqueue((basketId, update));
        return Task.CompletedTask;
    }
}
