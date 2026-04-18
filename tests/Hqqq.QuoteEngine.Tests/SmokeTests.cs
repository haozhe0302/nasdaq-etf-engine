using Hqqq.Infrastructure.Kafka;
using Hqqq.Infrastructure.Redis;

namespace Hqqq.QuoteEngine.Tests;

public class SmokeTests
{
    [Fact]
    public void KafkaConfigBuilder_ProducesValidConsumerConfig()
    {
        var options = new KafkaOptions
        {
            BootstrapServers = "broker:9092",
            ClientId = "test",
            ConsumerGroupPrefix = "qe",
        };

        var config = KafkaConfigBuilder.BuildConsumerConfig(options, "quote-engine");

        Assert.Equal("broker:9092", config.BootstrapServers);
        Assert.Equal("qe-quote-engine", config.GroupId);
    }

    [Fact]
    public void RedisKeys_BuildsExpectedPatterns()
    {
        Assert.Equal("hqqq:snapshot:HQQQ", RedisKeys.Snapshot("HQQQ"));
        Assert.Equal("hqqq:latest:AAPL", RedisKeys.LatestQuote("AAPL"));
    }
}
