using Hqqq.Contracts.Events;
using Hqqq.Infrastructure.Kafka;
using Hqqq.QuoteEngine.Consumers;
using Hqqq.QuoteEngine.Services;
using Hqqq.QuoteEngine.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hqqq.QuoteEngine.Tests.Consumers;

public class RawTickConsumerTests
{
    private static (RawTickConsumer consumer, RecordingRawTickSink sink) Build()
    {
        var sink = new RecordingRawTickSink();
        var consumer = new RawTickConsumer(
            Options.Create(new KafkaOptions()),
            new QuoteEngineOptions(),
            sink,
            NullLogger<RawTickConsumer>.Instance);
        return (consumer, sink);
    }

    [Fact]
    public async Task Handle_ValidRawTickV1_IsPublishedAsNormalizedTick()
    {
        var (consumer, sink) = Build();
        var ts = new DateTimeOffset(2026, 4, 16, 13, 30, 0, TimeSpan.Zero);
        var tick = new RawTickV1
        {
            Symbol = "AAPL",
            Last = 205m,
            Bid = 204.9m,
            Ask = 205.1m,
            Currency = "USD",
            Provider = "test",
            ProviderTimestamp = ts,
            IngressTimestamp = ts.AddMilliseconds(5),
            Sequence = 42,
        };

        var applied = await consumer.HandleAsync(tick, CancellationToken.None);

        Assert.True(applied);
        var normalized = Assert.Single(sink.Published);
        Assert.Equal("AAPL", normalized.Symbol);
        Assert.Equal(205m, normalized.Last);
        Assert.Equal(204.9m, normalized.Bid);
        Assert.Equal(205.1m, normalized.Ask);
        Assert.Equal(42, normalized.Sequence);
        Assert.Equal(ts, normalized.ProviderTimestamp);
    }

    [Fact]
    public async Task Handle_NullValue_IsSkipped()
    {
        var (consumer, sink) = Build();

        var applied = await consumer.HandleAsync(null, CancellationToken.None);

        Assert.False(applied);
        Assert.Empty(sink.Published);
    }

    [Fact]
    public async Task Handle_EmptySymbol_IsSkipped()
    {
        var (consumer, sink) = Build();
        var ts = DateTimeOffset.UtcNow;
        var tick = new RawTickV1
        {
            Symbol = "   ",
            Last = 100m,
            Currency = "USD",
            Provider = "test",
            ProviderTimestamp = ts,
            IngressTimestamp = ts,
            Sequence = 1,
        };

        var applied = await consumer.HandleAsync(tick, CancellationToken.None);

        Assert.False(applied);
        Assert.Empty(sink.Published);
    }

    [Fact]
    public async Task Handle_NegativeSequence_IsSkipped()
    {
        var (consumer, sink) = Build();
        var ts = DateTimeOffset.UtcNow;
        var tick = new RawTickV1
        {
            Symbol = "AAPL",
            Last = 100m,
            Currency = "USD",
            Provider = "test",
            ProviderTimestamp = ts,
            IngressTimestamp = ts,
            Sequence = -1,
        };

        var applied = await consumer.HandleAsync(tick, CancellationToken.None);

        Assert.False(applied);
        Assert.Empty(sink.Published);
    }
}
