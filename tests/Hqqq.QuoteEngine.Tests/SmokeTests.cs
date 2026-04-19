using Confluent.Kafka;
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
    public void BuildConsumerConfig_NoAuth_LeavesSecurityProtocolDefault()
    {
        var options = new KafkaOptions
        {
            BootstrapServers = "broker:9092",
        };

        var config = KafkaConfigBuilder.BuildConsumerConfig(options, "quote-engine");

        Assert.Null(config.SecurityProtocol);
        Assert.Null(config.SaslMechanism);
        Assert.Null(config.SaslUsername);
        Assert.Null(config.SaslPassword);
    }

    [Fact]
    public void BuildConsumerConfig_WithSasl_AppliesAuth()
    {
        var options = new KafkaOptions
        {
            BootstrapServers = "ns.servicebus.windows.net:9093",
            SecurityProtocol = "SaslSsl",
            SaslMechanism = "Plain",
            SaslUsername = "$ConnectionString",
            SaslPassword = "Endpoint=sb://ns.servicebus.windows.net/;SharedAccessKey=...",
        };

        var config = KafkaConfigBuilder.BuildConsumerConfig(options, "quote-engine");

        Assert.Equal(SecurityProtocol.SaslSsl, config.SecurityProtocol);
        Assert.Equal(SaslMechanism.Plain, config.SaslMechanism);
        Assert.Equal("$ConnectionString", config.SaslUsername);
        Assert.Equal("Endpoint=sb://ns.servicebus.windows.net/;SharedAccessKey=...", config.SaslPassword);
    }

    [Fact]
    public void BuildProducerConfig_WithSasl_AppliesAuth()
    {
        var options = new KafkaOptions
        {
            BootstrapServers = "ns.servicebus.windows.net:9093",
            SecurityProtocol = "SaslSsl",
            SaslMechanism = "Plain",
            SaslUsername = "$ConnectionString",
            SaslPassword = "Endpoint=sb://ns.servicebus.windows.net/;SharedAccessKey=...",
        };

        var config = KafkaConfigBuilder.BuildProducerConfig(options);

        Assert.Equal(SecurityProtocol.SaslSsl, config.SecurityProtocol);
        Assert.Equal(SaslMechanism.Plain, config.SaslMechanism);
        Assert.Equal("$ConnectionString", config.SaslUsername);
        Assert.Equal("Endpoint=sb://ns.servicebus.windows.net/;SharedAccessKey=...", config.SaslPassword);
    }

    [Fact]
    public void RedisKeys_BuildsExpectedPatterns()
    {
        Assert.Equal("hqqq:snapshot:HQQQ", RedisKeys.Snapshot("HQQQ"));
        Assert.Equal("hqqq:latest:AAPL", RedisKeys.LatestQuote("AAPL"));
    }
}
