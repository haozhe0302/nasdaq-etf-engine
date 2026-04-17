using Hqqq.Contracts.Events;
using Hqqq.Infrastructure.Kafka;
using Hqqq.QuoteEngine.Publishing;
using Hqqq.QuoteEngine.Services;
using Hqqq.QuoteEngine.Tests.Fakes;

namespace Hqqq.QuoteEngine.Tests.Publishing;

public class SnapshotTopicPublisherTests
{
    private static QuoteSnapshotV1 SampleEvent(string basketId = "HQQQ") => new()
    {
        BasketId = basketId,
        Timestamp = new DateTimeOffset(2026, 4, 16, 13, 30, 0, TimeSpan.Zero),
        Nav = 600m,
        MarketProxyPrice = 500m,
        PremiumDiscountPct = -16.6667m,
        StaleCount = 0,
        FreshCount = 3,
        MaxComponentAgeMs = 42d,
        QuoteQuality = "live",
    };

    [Fact]
    public async Task PublishAsync_UsesConfiguredTopicAndBasketIdKey()
    {
        var producer = new RecordingPricingSnapshotProducer();
        var publisher = new SnapshotTopicPublisher(producer, new QuoteEngineOptions());

        await publisher.PublishAsync(SampleEvent(), CancellationToken.None);

        var record = Assert.Single(producer.Published);
        Assert.Equal(KafkaTopics.PricingSnapshots, record.Topic);
        Assert.Equal("pricing.snapshots.v1", record.Topic);
        Assert.Equal("HQQQ", record.Key);
        Assert.Equal(600m, record.Value.Nav);
        Assert.Equal("live", record.Value.QuoteQuality);
    }

    [Fact]
    public async Task PublishAsync_HonorsOverriddenTopic()
    {
        var producer = new RecordingPricingSnapshotProducer();
        var options = new QuoteEngineOptions
        {
            PricingSnapshotsTopic = "pricing.snapshots.staging",
        };
        var publisher = new SnapshotTopicPublisher(producer, options);

        await publisher.PublishAsync(SampleEvent(), CancellationToken.None);

        var record = Assert.Single(producer.Published);
        Assert.Equal("pricing.snapshots.staging", record.Topic);
    }

    [Fact]
    public async Task PublishAsync_RejectsEventWithMissingBasketId()
    {
        var producer = new RecordingPricingSnapshotProducer();
        var publisher = new SnapshotTopicPublisher(producer, new QuoteEngineOptions());
        var evt = SampleEvent(basketId: " ");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            publisher.PublishAsync(evt, CancellationToken.None));
        Assert.Empty(producer.Published);
    }
}
