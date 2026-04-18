using Hqqq.Infrastructure.Kafka;
using Hqqq.QuoteEngine.Consumers;
using Hqqq.QuoteEngine.Services;
using Hqqq.QuoteEngine.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hqqq.QuoteEngine.Tests.Consumers;

public class BasketEventConsumerTests
{
    private static (BasketEventConsumer consumer, RecordingBasketStateSink sink) Build()
    {
        var sink = new RecordingBasketStateSink();
        var consumer = new BasketEventConsumer(
            Options.Create(new KafkaOptions()),
            new QuoteEngineOptions(),
            sink,
            NullLogger<BasketEventConsumer>.Instance);
        return (consumer, sink);
    }

    [Fact]
    public async Task Handle_MapsBasketActiveStateV1ToActiveBasket_AndPublishesToSink()
    {
        var (consumer, sink) = Build();
        var state = new TestActiveBasketStateBuilder()
            .WithBasketId("HQQQ").WithFingerprint("fp-1").WithScaleFactor(0.001m)
            .WithNavPreviousClose(550m)
            .AddConstituent("AAPL", "Apple", 1000, 200m, 0.5m)
            .AddConstituent("MSFT", "Microsoft", 500, 400m, 0.5m)
            .Build();

        var applied = await consumer.HandleAsync(state, CancellationToken.None);

        Assert.True(applied);
        Assert.Single(sink.Published);
        var basket = sink.Published.Single();
        Assert.Equal("HQQQ", basket.BasketId);
        Assert.Equal("fp-1", basket.Fingerprint);
        Assert.Equal(0.001m, basket.ScaleFactor.Value);
        Assert.Equal(2, basket.Constituents.Count);
        Assert.Equal(2, basket.PricingBasis.Entries.Count);
        Assert.Equal(550m, basket.NavPreviousClose);
        Assert.Equal("fp-1", consumer.LastAppliedFingerprint);
    }

    [Fact]
    public async Task Handle_DifferentFingerprint_ReplacesActiveBasket()
    {
        var (consumer, sink) = Build();

        var first = new TestActiveBasketStateBuilder()
            .WithFingerprint("fp-1")
            .AddConstituent("AAPL", "Apple", 1000, 200m, 1.0m)
            .Build();
        var second = new TestActiveBasketStateBuilder()
            .WithFingerprint("fp-2").WithScaleFactor(0.0005m)
            .AddConstituent("MSFT", "Microsoft", 500, 400m, 1.0m)
            .Build();

        Assert.True(await consumer.HandleAsync(first, CancellationToken.None));
        Assert.True(await consumer.HandleAsync(second, CancellationToken.None));

        Assert.Equal(2, sink.Published.Count);
        Assert.Equal("fp-2", consumer.LastAppliedFingerprint);
    }

    [Fact]
    public async Task Handle_SameFingerprintTwice_PublishesOnceThenSkips()
    {
        var (consumer, sink) = Build();

        var state = new TestActiveBasketStateBuilder()
            .WithFingerprint("fp-sticky")
            .AddConstituent("AAPL", "Apple", 1000, 200m, 1.0m)
            .Build();

        Assert.True(await consumer.HandleAsync(state, CancellationToken.None));
        Assert.False(await consumer.HandleAsync(state, CancellationToken.None));

        Assert.Single(sink.Published);
    }

    [Fact]
    public async Task Handle_PrimedFromRestoredFingerprint_SkipsMatchingReplay()
    {
        var (consumer, sink) = Build();
        consumer.PrimeFromRestoredFingerprint("fp-restored");

        var state = new TestActiveBasketStateBuilder()
            .WithFingerprint("fp-restored")
            .AddConstituent("AAPL", "Apple", 1000, 200m, 1.0m)
            .Build();

        var applied = await consumer.HandleAsync(state, CancellationToken.None);

        Assert.False(applied);
        Assert.Empty(sink.Published);
    }

    [Fact]
    public async Task Handle_NonPositiveScaleFactor_IsDroppedAndNotRemembered()
    {
        var (consumer, sink) = Build();
        var state = new TestActiveBasketStateBuilder()
            .WithFingerprint("fp-bad").WithScaleFactor(0m)
            .AddConstituent("AAPL", "Apple", 1000, 200m, 1.0m)
            .Build();

        var applied = await consumer.HandleAsync(state, CancellationToken.None);

        Assert.False(applied);
        Assert.Empty(sink.Published);
        Assert.Null(consumer.LastAppliedFingerprint);
    }

    [Fact]
    public async Task Handle_MissingConstituents_IsDropped()
    {
        var (consumer, sink) = Build();
        var state = new TestActiveBasketStateBuilder()
            .WithFingerprint("fp-empty")
            .Build();

        var applied = await consumer.HandleAsync(state, CancellationToken.None);

        Assert.False(applied);
        Assert.Empty(sink.Published);
    }

    [Fact]
    public async Task Handle_NullValue_IsSkipped()
    {
        var (consumer, sink) = Build();

        var applied = await consumer.HandleAsync(null, CancellationToken.None);

        Assert.False(applied);
        Assert.Empty(sink.Published);
    }
}
