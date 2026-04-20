using Hqqq.Contracts.Events;
using Hqqq.Infrastructure.Kafka;
using Hqqq.Ingress.Publishing;

namespace Hqqq.Ingress.Tests;

/// <summary>
/// Round-trip tests for <see cref="KafkaTickPublisher"/> using an
/// in-memory producer. Validates the public contract documented on
/// <see cref="ITickPublisher"/>: every call fans out to both the raw-tick
/// time-series topic and the latest-by-symbol compacted topic, both
/// keyed by symbol.
/// </summary>
public class KafkaTickPublisherTests
{
    [Fact]
    public async Task PublishAsync_FansOutToBothTopicsKeyedBySymbol()
    {
        var rawProducer = new InMemoryProducer<string, RawTickV1>();
        var latestProducer = new InMemoryProducer<string, LatestSymbolQuoteV1>();
        var publisher = new KafkaTickPublisher(rawProducer, latestProducer);

        var tick = SampleTick("AAPL", last: 215.30m);
        await publisher.PublishAsync(tick, CancellationToken.None);

        var raw = Assert.Single(rawProducer.Produced);
        Assert.Equal(KafkaTopics.RawTicks, raw.Topic);
        Assert.Equal("AAPL", raw.Key);
        Assert.Equal(215.30m, raw.Value.Last);

        var latest = Assert.Single(latestProducer.Produced);
        Assert.Equal(KafkaTopics.LatestBySymbol, latest.Topic);
        Assert.Equal("AAPL", latest.Key);
        Assert.Equal(215.30m, latest.Value.Last);
        Assert.False(latest.Value.IsStale);
    }

    [Fact]
    public async Task PublishAsync_LatestQuoteCarriesAllScalarFields()
    {
        var rawProducer = new InMemoryProducer<string, RawTickV1>();
        var latestProducer = new InMemoryProducer<string, LatestSymbolQuoteV1>();
        var publisher = new KafkaTickPublisher(rawProducer, latestProducer);

        var tick = SampleTick("MSFT", last: 432.10m);
        await publisher.PublishAsync(tick, CancellationToken.None);

        var latest = latestProducer.Produced.Single().Value;
        Assert.Equal(tick.Symbol, latest.Symbol);
        Assert.Equal(tick.Last, latest.Last);
        Assert.Equal(tick.Bid, latest.Bid);
        Assert.Equal(tick.Ask, latest.Ask);
        Assert.Equal(tick.Currency, latest.Currency);
        Assert.Equal(tick.Provider, latest.Provider);
        Assert.Equal(tick.ProviderTimestamp, latest.ProviderTimestamp);
        Assert.Equal(tick.IngressTimestamp, latest.IngressTimestamp);
    }

    [Fact]
    public async Task PublishBatchAsync_ProducesOncePerTickPerTopic()
    {
        var rawProducer = new InMemoryProducer<string, RawTickV1>();
        var latestProducer = new InMemoryProducer<string, LatestSymbolQuoteV1>();
        var publisher = new KafkaTickPublisher(rawProducer, latestProducer);

        var ticks = new[]
        {
            SampleTick("AAPL", 215m),
            SampleTick("MSFT", 432m),
            SampleTick("NVDA", 950m),
        };

        await publisher.PublishBatchAsync(ticks, CancellationToken.None);

        Assert.Equal(3, rawProducer.Produced.Count);
        Assert.Equal(3, latestProducer.Produced.Count);
        Assert.Equal(
            new[] { "AAPL", "MSFT", "NVDA" },
            rawProducer.Produced.Select(p => p.Key).ToArray());
        Assert.Equal(
            new[] { "AAPL", "MSFT", "NVDA" },
            latestProducer.Produced.Select(p => p.Key).ToArray());
    }

    private static RawTickV1 SampleTick(string symbol, decimal last)
    {
        var providerTime = new DateTimeOffset(2026, 4, 17, 14, 30, 0, TimeSpan.Zero);
        return new RawTickV1
        {
            Symbol = symbol,
            Last = last,
            Bid = last - 0.05m,
            Ask = last + 0.05m,
            Currency = "USD",
            Provider = "tiingo",
            ProviderTimestamp = providerTime,
            IngressTimestamp = providerTime.AddSeconds(1),
            Sequence = 1,
        };
    }
}
