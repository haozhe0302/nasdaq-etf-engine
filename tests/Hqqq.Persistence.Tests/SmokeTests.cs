using Hqqq.Infrastructure.Kafka;
using Hqqq.Infrastructure.Timescale;

namespace Hqqq.Persistence.Tests;

public class SmokeTests
{
    [Fact]
    public void KafkaOptions_DefaultsAreReasonable()
    {
        var opts = new KafkaOptions();
        Assert.False(string.IsNullOrWhiteSpace(opts.BootstrapServers));
        Assert.False(string.IsNullOrWhiteSpace(opts.ConsumerGroupPrefix));
    }

    [Fact]
    public void TimescaleOptions_DefaultConnectionString_ContainsHost()
    {
        var opts = new TimescaleOptions();
        Assert.Contains("Host=", opts.ConnectionString);
        Assert.Contains("Database=", opts.ConnectionString);
    }

    [Fact]
    public void KafkaConfigBuilder_ProducesValidProducerConfig()
    {
        var options = new KafkaOptions
        {
            BootstrapServers = "broker:9092",
            ClientId = "persistence",
        };

        var config = KafkaConfigBuilder.BuildProducerConfig(options);

        Assert.Equal("broker:9092", config.BootstrapServers);
        Assert.True(config.EnableIdempotence);
    }
}
