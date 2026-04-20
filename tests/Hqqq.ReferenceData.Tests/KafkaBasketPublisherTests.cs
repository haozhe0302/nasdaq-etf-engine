using Hqqq.Contracts.Events;
using Hqqq.Infrastructure.Kafka;
using Hqqq.ReferenceData.Publishing;
using Hqqq.ReferenceData.Standalone;

namespace Hqqq.ReferenceData.Tests;

/// <summary>
/// Round-trip tests for <see cref="KafkaBasketPublisher"/> using an
/// in-memory producer. Validates that the active-basket event is sent
/// to <see cref="KafkaTopics.BasketActive"/> keyed by basket id and
/// carries the full mapped payload.
/// </summary>
public class KafkaBasketPublisherTests
{
    [Fact]
    public async Task PublishAsync_SendsToBasketActiveTopicKeyedByBasketId()
    {
        var fake = new InMemoryProducer<string, BasketActiveStateV1>();
        var publisher = new KafkaBasketPublisher(fake);

        var ev = BasketSeedToEventMapper.ToEvent(BuildSeed(), DateTimeOffset.UtcNow);
        await publisher.PublishAsync(ev, CancellationToken.None);

        var produced = Assert.Single(fake.Produced);
        Assert.Equal(KafkaTopics.BasketActive, produced.Topic);
        Assert.Equal("HQQQ", produced.Key);
        Assert.Equal(ev.Fingerprint, produced.Value.Fingerprint);
        Assert.Equal(ev.Constituents.Count, produced.Value.Constituents.Count);
        Assert.NotEmpty(produced.Value.PricingBasis.Entries);
        Assert.True(produced.Value.ScaleFactor > 0);
    }

    [Fact]
    public async Task PublishAsync_PropagatesProducerFailures()
    {
        var fake = new InMemoryProducer<string, BasketActiveStateV1>
        {
            FailWith = new InvalidOperationException("broker down"),
        };
        var publisher = new KafkaBasketPublisher(fake);

        var ev = BasketSeedToEventMapper.ToEvent(BuildSeed(), DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => publisher.PublishAsync(ev, CancellationToken.None));
    }

    private static BasketSeed BuildSeed() => new()
    {
        BasketId = "HQQQ",
        Version = "v-test",
        AsOfDate = new DateOnly(2026, 4, 15),
        ScaleFactor = 1.0m,
        Fingerprint = "deadbeef" + new string('0', 56),
        Source = "test://memory",
        Constituents = new List<BasketSeedConstituent>
        {
            new() { Symbol = "AAPL", Name = "Apple", Sector = "Technology", SharesHeld = 100, ReferencePrice = 215.30m },
            new() { Symbol = "MSFT", Name = "Microsoft", Sector = "Technology", SharesHeld = 100, ReferencePrice = 432.10m },
        },
    };
}
