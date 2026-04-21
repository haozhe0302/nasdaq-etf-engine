using Hqqq.Gateway.Services.Realtime;

namespace Hqqq.Gateway.Tests.Fixtures;

/// <summary>
/// Test double for <see cref="IRedisQuoteUpdateChannel"/>. Each attempt at
/// <see cref="SubscribeAsync"/> calls <see cref="_factoryPerAttempt"/>; the
/// delegate can throw to simulate a transient Redis failure or return to
/// simulate a successful subscription. <see cref="PublishAsync"/> drives a
/// payload into whatever handler the live subscription registered.
/// </summary>
public sealed class FakeRedisQuoteUpdateChannel : IRedisQuoteUpdateChannel
{
    private readonly Action<int> _factoryPerAttempt;
    private Func<string, CancellationToken, Task>? _handler;
    private int _attemptCount;

    public FakeRedisQuoteUpdateChannel(Action<int> factoryPerAttempt)
    {
        _factoryPerAttempt = factoryPerAttempt;
    }

    public int AttemptCount => Volatile.Read(ref _attemptCount);
    public bool Subscribed { get; private set; }
    public bool Unsubscribed { get; private set; }

    public Task SubscribeAsync(
        Func<string, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var attempt = Interlocked.Increment(ref _attemptCount);

        // Throwing here is how the fixture simulates a Redis transport
        // failure — the subscriber must catch it and retry with backoff.
        _factoryPerAttempt(attempt);

        _handler = handler;
        Subscribed = true;
        Unsubscribed = false;
        return Task.CompletedTask;
    }

    public Task UnsubscribeAsync(CancellationToken cancellationToken)
    {
        _handler = null;
        Subscribed = false;
        Unsubscribed = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Drives a payload into the registered handler as if it had arrived
    /// on the Redis channel. Only valid after a successful subscribe.
    /// </summary>
    public Task PublishAsync(string payload)
    {
        var handler = _handler
            ?? throw new InvalidOperationException(
                "FakeRedisQuoteUpdateChannel has no active subscriber.");
        return handler(payload, CancellationToken.None);
    }
}
