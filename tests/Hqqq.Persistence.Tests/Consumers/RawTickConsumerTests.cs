using Hqqq.Contracts.Events;
using Hqqq.Infrastructure.Kafka;
using Hqqq.Persistence.Consumers;
using Hqqq.Persistence.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Hqqq.Persistence.Tests.Consumers;

public class RawTickConsumerTests
{
    private static (RawTickConsumer consumer, RecordingRawTickSink sink) Build()
    {
        var sink = new RecordingRawTickSink();
        var consumer = new RawTickConsumer(
            MsOptions.Create(new KafkaOptions()),
            sink,
            NullLogger<RawTickConsumer>.Instance);
        return (consumer, sink);
    }

    private static RawTickV1 Sample(
        string symbol = "NVDA",
        long sequence = 1,
        string currency = "USD",
        string provider = "tiingo",
        DateTimeOffset? providerTs = null,
        DateTimeOffset? ingressTs = null) => new()
        {
            Symbol = symbol,
            Last = 900m,
            Bid = 899.5m,
            Ask = 900.5m,
            Currency = currency,
            Provider = provider,
            ProviderTimestamp = providerTs ?? new DateTimeOffset(2026, 4, 16, 13, 30, 0, TimeSpan.Zero),
            IngressTimestamp = ingressTs ?? new DateTimeOffset(2026, 4, 16, 13, 30, 0, 50, TimeSpan.Zero),
            Sequence = sequence,
        };

    [Fact]
    public async Task Handle_ValidTick_IsPublishedToSink()
    {
        var (consumer, sink) = Build();

        var accepted = await consumer.HandleAsync(Sample(), CancellationToken.None);

        Assert.True(accepted);
        var forwarded = Assert.Single(sink.Published);
        Assert.Equal("NVDA", forwarded.Symbol);
        Assert.Equal(900m, forwarded.Last);
    }

    [Fact]
    public async Task Handle_NullValue_IsSkipped()
    {
        var (consumer, sink) = Build();

        var accepted = await consumer.HandleAsync(null, CancellationToken.None);

        Assert.False(accepted);
        Assert.Empty(sink.Published);
    }

    [Fact]
    public async Task Handle_EmptySymbol_IsSkipped()
    {
        var (consumer, sink) = Build();

        var accepted = await consumer.HandleAsync(Sample(symbol: "   "), CancellationToken.None);

        Assert.False(accepted);
        Assert.Empty(sink.Published);
    }

    [Fact]
    public async Task Handle_NegativeSequence_IsSkipped()
    {
        var (consumer, sink) = Build();

        var accepted = await consumer.HandleAsync(Sample(sequence: -1), CancellationToken.None);

        Assert.False(accepted);
        Assert.Empty(sink.Published);
    }

    [Fact]
    public async Task Handle_DefaultProviderTimestamp_IsSkipped()
    {
        var (consumer, sink) = Build();

        var accepted = await consumer.HandleAsync(
            Sample(providerTs: default(DateTimeOffset)), CancellationToken.None);

        Assert.False(accepted);
        Assert.Empty(sink.Published);
    }

    [Fact]
    public async Task Handle_DefaultIngressTimestamp_IsSkipped()
    {
        var (consumer, sink) = Build();

        var accepted = await consumer.HandleAsync(
            Sample(ingressTs: default(DateTimeOffset)), CancellationToken.None);

        Assert.False(accepted);
        Assert.Empty(sink.Published);
    }

    [Fact]
    public async Task Handle_EmptyCurrency_IsSkipped()
    {
        var (consumer, sink) = Build();

        var accepted = await consumer.HandleAsync(Sample(currency: ""), CancellationToken.None);

        Assert.False(accepted);
        Assert.Empty(sink.Published);
    }

    [Fact]
    public async Task Handle_EmptyProvider_IsSkipped()
    {
        var (consumer, sink) = Build();

        var accepted = await consumer.HandleAsync(Sample(provider: " "), CancellationToken.None);

        Assert.False(accepted);
        Assert.Empty(sink.Published);
    }

    [Fact]
    public async Task Handle_DoesNotThrow_OnMalformedInputs()
    {
        var (consumer, _) = Build();

        await consumer.HandleAsync(null, CancellationToken.None);
        await consumer.HandleAsync(Sample(symbol: ""), CancellationToken.None);
        await consumer.HandleAsync(Sample(sequence: -5), CancellationToken.None);
        await consumer.HandleAsync(Sample(providerTs: default), CancellationToken.None);
        await consumer.HandleAsync(Sample(currency: ""), CancellationToken.None);
        await consumer.HandleAsync(Sample(provider: ""), CancellationToken.None);
    }
}
