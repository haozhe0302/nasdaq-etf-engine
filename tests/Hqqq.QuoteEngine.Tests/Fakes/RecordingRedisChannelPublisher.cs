using System.Collections.Concurrent;
using Hqqq.QuoteEngine.Publishing;

namespace Hqqq.QuoteEngine.Tests.Fakes;

/// <summary>
/// Recording <see cref="IRedisChannelPublisher"/> for unit tests of the
/// quote-update publisher path. Captures every PUBLISH call and can be
/// configured to throw to exercise the failure-isolation contract.
/// </summary>
public sealed class RecordingRedisChannelPublisher : IRedisChannelPublisher
{
    private readonly ConcurrentQueue<(string Channel, string Payload)> _published = new();

    public IReadOnlyCollection<(string Channel, string Payload)> Published => _published;

    public Exception? ThrowOnPublish { get; set; }

    public Task PublishAsync(string channel, string payload, CancellationToken ct)
    {
        if (ThrowOnPublish is { } ex)
            throw ex;

        _published.Enqueue((channel, payload));
        return Task.CompletedTask;
    }
}
