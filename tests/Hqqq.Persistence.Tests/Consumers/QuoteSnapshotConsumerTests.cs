using Hqqq.Contracts.Events;
using Hqqq.Infrastructure.Kafka;
using Hqqq.Observability.Metrics;
using Hqqq.Persistence.Consumers;
using Hqqq.Persistence.MarketSession;
using Hqqq.Persistence.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Hqqq.Persistence.Tests.Consumers;

public class QuoteSnapshotConsumerTests
{
    private static readonly TimeZoneInfo Eastern = RegularSessionClock.ResolveEasternTimeZone();

    private static (QuoteSnapshotConsumer consumer, RecordingQuoteSnapshotSink sink) Build()
    {
        var sink = new RecordingQuoteSnapshotSink();
        var consumer = new QuoteSnapshotConsumer(
            MsOptions.Create(new KafkaOptions()),
            sink,
            new RegularSessionClock(Eastern),
            new HqqqMetrics(),
            NullLogger<QuoteSnapshotConsumer>.Instance);
        return (consumer, sink);
    }

    /// <summary>Builds a UTC instant from an ET wall-clock time on the given date.</summary>
    private static DateTimeOffset Et(int year, int month, int day, int hour, int minute)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        var offset = Eastern.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    private static QuoteSnapshotV1 Sample(
        string basketId = "HQQQ",
        string quality = "live",
        DateTimeOffset? ts = null) => new()
        {
            BasketId = basketId,
            // Default ts is 13:30 UTC = 09:30 ET (EDT) on a weekday — in session.
            Timestamp = ts ?? Et(2026, 4, 16, 12, 0),
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
    public async Task Handle_AtSessionOpen_IsPublished()
    {
        var (consumer, sink) = Build();

        var accepted = await consumer.HandleAsync(Sample(ts: Et(2026, 4, 16, 9, 30)), CancellationToken.None);

        Assert.True(accepted);
        Assert.Single(sink.Published);
    }

    [Fact]
    public async Task Handle_BeforeSessionOpen_IsSkipped()
    {
        var (consumer, sink) = Build();

        var accepted = await consumer.HandleAsync(Sample(ts: Et(2026, 4, 16, 9, 29)), CancellationToken.None);

        Assert.False(accepted);
        Assert.Empty(sink.Published);
    }

    [Fact]
    public async Task Handle_AtSessionClose_IsSkipped()
    {
        // 16:00 ET exactly is the exclusive close — out of session.
        var (consumer, sink) = Build();

        var accepted = await consumer.HandleAsync(Sample(ts: Et(2026, 4, 16, 16, 0)), CancellationToken.None);

        Assert.False(accepted);
        Assert.Empty(sink.Published);
    }

    [Fact]
    public async Task Handle_AfterHours_IsSkipped()
    {
        var (consumer, sink) = Build();

        var accepted = await consumer.HandleAsync(Sample(ts: Et(2026, 4, 16, 18, 0)), CancellationToken.None);

        Assert.False(accepted);
        Assert.Empty(sink.Published);
    }

    [Fact]
    public async Task Handle_SaturdayDuringSessionHours_IsSkipped()
    {
        // 2026-04-18 is a Saturday; noon ET must not be persisted.
        var (consumer, sink) = Build();

        var accepted = await consumer.HandleAsync(Sample(ts: Et(2026, 4, 18, 12, 0)), CancellationToken.None);

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
