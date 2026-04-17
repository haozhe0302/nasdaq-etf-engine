using System.Threading.Channels;
using Hqqq.QuoteEngine.Abstractions;
using Hqqq.QuoteEngine.Models;

namespace Hqqq.QuoteEngine.Tests.Fakes;

/// <summary>
/// Minimal bounded-channel tick feed used by worker-level tests. The
/// pre-seeded queue is drained by <see cref="ConsumeAsync"/>; tests call
/// <see cref="Complete"/> to stop the consumer after it has drained.
/// </summary>
public sealed class FakeRawTickFeed : IRawTickFeed
{
    private readonly Channel<NormalizedTick> _channel =
        Channel.CreateUnbounded<NormalizedTick>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    public void Enqueue(NormalizedTick tick) => _channel.Writer.TryWrite(tick);

    public void EnqueueRange(IEnumerable<NormalizedTick> ticks)
    {
        foreach (var t in ticks) _channel.Writer.TryWrite(t);
    }

    public void Complete() => _channel.Writer.TryComplete();

    public async IAsyncEnumerable<NormalizedTick> ConsumeAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        while (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
        {
            while (_channel.Reader.TryRead(out var tick))
                yield return tick;
        }
    }
}
