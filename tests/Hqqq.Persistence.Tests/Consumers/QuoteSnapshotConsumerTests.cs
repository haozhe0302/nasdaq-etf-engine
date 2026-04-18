using Hqqq.Contracts.Events;
using Hqqq.Infrastructure.Kafka;
using Hqqq.Persistence.Consumers;
using Hqqq.Persistence.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Hqqq.Persistence.Tests.Consumers;

public class QuoteSnapshotConsumerTests
{
    private static (QuoteSnapshotConsumer consumer, RecordingQuoteSnapshotSink sink) Build()
    {
        var sink = new RecordingQuoteSnapshotSink();
        var consumer = new QuoteSnapshotConsumer(
            MsOptions.Create(new KafkaOptions()),
            sink,
            NullLogger<QuoteSnapshotConsumer>.Instance);
        return (consumer, sink);
    }

    private static QuoteSnapshotV1 Sample(
        string basketId = "HQQQ",
        string quality = "live",
        DateTimeOffset? ts = null) => new()
        {
            BasketId = basketId,
            Timestamp = ts ?? new DateTimeOffset(2026, 4, 16, 13, 30, 0, TimeSpan.Zero),
            Nav = 600m,
            MarketProxyPrice = 500m,
            PremiumDiscountPct = -16.6667m,
            StaleCount = 0,
            FreshCount = 3,
            MaxComponentAgeMs = 42d,
            QuoteQuality = quality,
        };

    [Fact]
    public async Task Handle_ValidSnapshot_IsPublishedToSink()
    {
        var (consumer, sink) = Build();

        var accepted = await consumer.HandleAsync(Sample(), CancellationToken.None);

        Assert.True(accepted);
        var forwarded = Assert.Single(sink.Published);
        Assert.Equal("HQQQ", forwarded.BasketId);
        Assert.Equal(600m, forwarded.Nav);
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
    public async Task Handle_EmptyBasketId_IsSkipped()
    {
        var (consumer, sink) = Build();

        var accepted = await consumer.HandleAsync(Sample(basketId: "   "), CancellationToken.None);

        Assert.False(accepted);
        Assert.Empty(sink.Published);
    }

    [Fact]
    public async Task Handle_DefaultTimestamp_IsSkipped()
    {
        var (consumer, sink) = Build();

        var accepted = await consumer.HandleAsync(
            Sample(ts: default(DateTimeOffset)), CancellationToken.None);

        Assert.False(accepted);
        Assert.Empty(sink.Published);
    }

    [Fact]
    public async Task Handle_EmptyQuality_IsSkipped()
    {
        var (consumer, sink) = Build();

        var accepted = await consumer.HandleAsync(Sample(quality: ""), CancellationToken.None);

        Assert.False(accepted);
        Assert.Empty(sink.Published);
    }

    [Fact]
    public async Task Handle_DoesNotThrow_OnMalformedInputs()
    {
        // Malformed events must never take down the consumer loop.
        var (consumer, _) = Build();

        await consumer.HandleAsync(null, CancellationToken.None);
        await consumer.HandleAsync(Sample(basketId: ""), CancellationToken.None);
        await consumer.HandleAsync(Sample(quality: " "), CancellationToken.None);
        await consumer.HandleAsync(Sample(ts: default), CancellationToken.None);
    }
}
