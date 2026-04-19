using Hqqq.Infrastructure.Hosting;
using Hqqq.Infrastructure.Kafka;
using Hqqq.Infrastructure.Redis;
using Hqqq.Infrastructure.Timescale;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hqqq.Infrastructure.Tests;

public class ConfigBindingTests
{
    [Fact]
    public void KafkaOptions_BindsFromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kafka:BootstrapServers"] = "broker1:9092",
                ["Kafka:ClientId"] = "test-client",
                ["Kafka:ConsumerGroupPrefix"] = "test-prefix",
                ["Kafka:SecurityProtocol"] = "SaslSsl",
                ["Kafka:SaslMechanism"] = "Plain",
                ["Kafka:SaslUsername"] = "$ConnectionString",
                ["Kafka:SaslPassword"] = "Endpoint=sb://example.servicebus.windows.net/;SharedAccessKey=...",
                ["Kafka:EnableTopicBootstrap"] = "false",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddHqqqKafka(config);

        var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<KafkaOptions>>().Value;

        Assert.Equal("broker1:9092", opts.BootstrapServers);
        Assert.Equal("test-client", opts.ClientId);
        Assert.Equal("test-prefix", opts.ConsumerGroupPrefix);
        Assert.Equal("SaslSsl", opts.SecurityProtocol);
        Assert.Equal("Plain", opts.SaslMechanism);
        Assert.Equal("$ConnectionString", opts.SaslUsername);
        Assert.Equal("Endpoint=sb://example.servicebus.windows.net/;SharedAccessKey=...", opts.SaslPassword);
        Assert.False(opts.EnableTopicBootstrap);
    }

    [Fact]
    public void RedisOptions_BindsFromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redis:Configuration"] = "redis-host:6380",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddHqqqRedis(config);

        var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<RedisOptions>>().Value;

        Assert.Equal("redis-host:6380", opts.Configuration);
    }

    [Fact]
    public void TimescaleOptions_BindsFromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Timescale:ConnectionString"] = "Host=myhost;Database=mydb",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddHqqqTimescale(config);

        var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<TimescaleOptions>>().Value;

        Assert.Equal("Host=myhost;Database=mydb", opts.ConnectionString);
    }

    [Fact]
    public void Options_HaveSensibleDefaults_WhenNoConfigProvided()
    {
        var config = new ConfigurationBuilder().Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddHqqqKafka(config);
        services.AddHqqqRedis(config);
        services.AddHqqqTimescale(config);

        var sp = services.BuildServiceProvider();

        var kafka = sp.GetRequiredService<IOptions<KafkaOptions>>().Value;
        Assert.Equal("localhost:9092", kafka.BootstrapServers);
        Assert.Equal("hqqq-local", kafka.ClientId);
        Assert.Null(kafka.SecurityProtocol);
        Assert.Null(kafka.SaslMechanism);
        Assert.Null(kafka.SaslUsername);
        Assert.Null(kafka.SaslPassword);
        Assert.True(kafka.EnableTopicBootstrap);

        var redis = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
        Assert.Equal("localhost:6379", redis.Configuration);

        var ts = sp.GetRequiredService<IOptions<TimescaleOptions>>().Value;
        Assert.Contains("localhost", ts.ConnectionString);
    }
}
