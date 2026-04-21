using Hqqq.Contracts.Events;
using Hqqq.Infrastructure.Kafka;
using Hqqq.ReferenceData.Publishing;

namespace Hqqq.ReferenceData.Tests.Publishing;

/// <summary>
/// Exercises the Kafka publisher through the in-memory producer seam so we
/// cover topic routing, key-by-basketId, and timestamp population without a
/// real broker.
/// </summary>
public class KafkaBasketPublisherTests
{
    [Fact]
    public async Task PublishAsync_UsesDefaultTopic_WhenNoOverrideConfigured()
    {
        var producer = new InMemoryProducer<string, BasketActiveStateV1>();
        var publisher = new KafkaBasketPublisher(producer);

        var state = BuildState();
        await publisher.PublishAsync(state, CancellationToken.None);

        Assert.Equal(KafkaTopics.BasketActive, publisher.Topic);
        var produced = Assert.Single(producer.Produced);
        Assert.Equal(KafkaTopics.BasketActive, produced.Topic);
        Assert.Equal("HQQQ", produced.Key);
        Assert.Equal(state, produced.Value);
    }

    [Fact]
    public async Task PublishAsync_UsesOverrideTopic_WhenProvided()
    {
        var producer = new InMemoryProducer<string, BasketActiveStateV1>();
        var publisher = new KafkaBasketPublisher(producer, topic: "custom.basket.active");

        await publisher.PublishAsync(BuildState(), CancellationToken.None);

        Assert.Equal("custom.basket.active", publisher.Topic);
        Assert.Equal("custom.basket.active", producer.Produced.Single().Topic);
    }

    [Fact]
    public async Task PublishAsync_SetsMessageTimestampToActivatedAtUtc()
    {
        var producer = new InMemoryProducer<string, BasketActiveStateV1>();
        var publisher = new KafkaBasketPublisher(producer);

        var state = BuildState() with
        {
            ActivatedAtUtc = new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero),
        };
        await publisher.PublishAsync(state, CancellationToken.None);

        var produced = producer.Produced.Single();
        Assert.Equal(state.ActivatedAtUtc.UtcDateTime, produced.Message.Timestamp.UtcDateTime);
    }

    private static BasketActiveStateV1 BuildState()
        => new()
        {
            BasketId = "HQQQ",
            Fingerprint = "abc123",
            Version = "v-test",
            AsOfDate = new DateOnly(2026, 4, 15),
            ActivatedAtUtc = DateTimeOffset.UtcNow,
            Constituents = Array.Empty<BasketConstituentV1>(),
            PricingBasis = new PricingBasisV1
            {
                PricingBasisFingerprint = "abc123",
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Entries = Array.Empty<PricingBasisEntryV1>(),
                InferredTotalNotional = 0m,
                OfficialSharesCount = 0,
                DerivedSharesCount = 0,
            },
            ScaleFactor = 1m,
            NavPreviousClose = 540m,
            QqqPreviousClose = 480m,
            Source = "fallback-seed",
            ConstituentCount = 0,
        };
}
