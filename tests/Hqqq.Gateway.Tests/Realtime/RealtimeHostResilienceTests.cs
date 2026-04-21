using System.Net;
using System.Text.Json;
using Hqqq.Gateway.Services.Realtime;
using Hqqq.Gateway.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace Hqqq.Gateway.Tests.Realtime;

/// <summary>
/// Phase 2-hotfix — in-process regression guard for the "degraded-not-crashed"
/// posture: even with realtime enabled, an unreachable Redis pub/sub
/// transport must not knock the gateway host over. <c>/api/system/health</c>
/// must still return 200 and roll up to a downstream-driven status.
/// </summary>
public class RealtimeHostResilienceTests
{
    private sealed class AlwaysFailingRedisChannel : IRedisQuoteUpdateChannel
    {
        public int Attempts;
        public Task SubscribeAsync(
            Func<string, CancellationToken, Task> handler,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Attempts);
            throw new StackExchange.Redis.RedisConnectionException(
                StackExchange.Redis.ConnectionFailureType.UnableToConnect,
                "test: Redis unavailable");
        }

        public Task UnsubscribeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task RealtimeEnabled_RedisUnavailable_HostStillServesHealth()
    {
        var failingChannel = new AlwaysFailingRedisChannel();
        var healthClient = new ScriptedServiceHealthClient()
            .SetHealthy("hqqq-reference-data")
            .SetHealthy("hqqq-ingress")
            .SetHealthy("hqqq-quote-engine")
            .SetHealthy("hqqq-persistence")
            .SetHealthy("hqqq-analytics");

        using var factory = new GatewayAppFactory()
            .WithConfig("Gateway:Realtime:Enabled", "true")
            .WithConfig("Gateway:Realtime:InitialRetryDelayMs", "25")
            .WithConfig("Gateway:Realtime:MaxRetryDelayMs", "100")
            .WithConfig("Gateway:DataSource", "stub")
            .WithConfig("Gateway:Sources:SystemHealth", "aggregated")
            .WithConfig("Gateway:Health:IncludeRedis", "false")
            .WithConfig("Gateway:Health:IncludeTimescale", "false")
            .WithConfig("Gateway:Health:Services:ReferenceData:BaseUrl", "http://refdata.test")
            .WithConfig("Gateway:Health:Services:Ingress:BaseUrl", "http://ingress.test")
            .WithConfig("Gateway:Health:Services:QuoteEngine:BaseUrl", "http://qe.test")
            .WithConfig("Gateway:Health:Services:Persistence:BaseUrl", "http://persist.test")
            .WithConfig("Gateway:Health:Services:Analytics:BaseUrl", "http://analytics.test")
            .WithFakeServiceHealthClient(healthClient);

        // Last-wins: swap the production Redis channel for one that always
        // throws RedisConnectionException so we don't need a real broker.
        factory.ConfigureTestServices(services =>
        {
            services.AddSingleton<IRedisQuoteUpdateChannel>(failingChannel);
        });

        using var http = factory.CreateClient();

        var response = await http.GetAsync("/api/system/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("healthy", doc.RootElement.GetProperty("status").GetString());

        // Give the subscriber a beat to attempt, fail, and retry at least
        // once — the host must survive every retry cycle.
        await Task.Delay(200);

        var response2 = await http.GetAsync("/api/system/health");
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

        Assert.True(failingChannel.Attempts >= 1,
            "QuoteUpdateSubscriber should have attempted to subscribe at least once.");
    }

    [Fact]
    public async Task RealtimeEnabled_RedisUnavailable_HealthRollsUpToDegraded_WhenDownstreamDegraded()
    {
        var failingChannel = new AlwaysFailingRedisChannel();
        var healthClient = new ScriptedServiceHealthClient()
            .SetHealthy("hqqq-reference-data")
            .SetUnreachable("hqqq-ingress", "unreachable: connection refused")
            .SetHealthy("hqqq-quote-engine")
            .SetHealthy("hqqq-persistence")
            .SetHealthy("hqqq-analytics");

        using var factory = new GatewayAppFactory()
            .WithConfig("Gateway:Realtime:Enabled", "true")
            .WithConfig("Gateway:Realtime:InitialRetryDelayMs", "25")
            .WithConfig("Gateway:Realtime:MaxRetryDelayMs", "100")
            .WithConfig("Gateway:DataSource", "stub")
            .WithConfig("Gateway:Sources:SystemHealth", "aggregated")
            .WithConfig("Gateway:Health:IncludeRedis", "false")
            .WithConfig("Gateway:Health:IncludeTimescale", "false")
            .WithConfig("Gateway:Health:Services:ReferenceData:BaseUrl", "http://refdata.test")
            .WithConfig("Gateway:Health:Services:Ingress:BaseUrl", "http://ingress.test")
            .WithConfig("Gateway:Health:Services:QuoteEngine:BaseUrl", "http://qe.test")
            .WithConfig("Gateway:Health:Services:Persistence:BaseUrl", "http://persist.test")
            .WithConfig("Gateway:Health:Services:Analytics:BaseUrl", "http://analytics.test")
            .WithFakeServiceHealthClient(healthClient);

        factory.ConfigureTestServices(services =>
        {
            services.AddSingleton<IRedisQuoteUpdateChannel>(failingChannel);
        });

        using var http = factory.CreateClient();

        var response = await http.GetAsync("/api/system/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("degraded", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task RealtimeDisabled_SubscriberDoesNotTouchRedis()
    {
        var failingChannel = new AlwaysFailingRedisChannel();

        using var factory = new GatewayAppFactory()
            .WithConfig("Gateway:Realtime:Enabled", "false")
            .WithConfig("Gateway:DataSource", "stub")
            .WithConfig("Gateway:Sources:SystemHealth", "aggregated")
            .WithConfig("Gateway:Health:IncludeRedis", "false")
            .WithConfig("Gateway:Health:IncludeTimescale", "false")
            .WithConfig("Gateway:Health:Services:Ingress:BaseUrl", "http://ingress.test")
            .WithConfig("Gateway:Health:Services:QuoteEngine:BaseUrl", "http://qe.test")
            .WithConfig("Gateway:Health:Services:Persistence:BaseUrl", "http://persist.test")
            .WithConfig("Gateway:Health:Services:Analytics:BaseUrl", "http://analytics.test")
            .WithConfig("Gateway:Health:Services:ReferenceData:BaseUrl", "http://refdata.test")
            .WithFakeServiceHealthClient(new ScriptedServiceHealthClient()
                .SetHealthy("hqqq-reference-data")
                .SetHealthy("hqqq-ingress")
                .SetHealthy("hqqq-quote-engine")
                .SetHealthy("hqqq-persistence")
                .SetHealthy("hqqq-analytics"));

        factory.ConfigureTestServices(services =>
        {
            services.AddSingleton<IRedisQuoteUpdateChannel>(failingChannel);
        });

        using var http = factory.CreateClient();

        var response = await http.GetAsync("/api/system/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Give the hosted service a beat to run its ExecuteAsync.
        await Task.Delay(150);

        // Behavioral contract: with realtime off, QuoteUpdateSubscriber
        // must never call into the Redis subscription seam. Even though
        // the hosted service is still registered, it exits cleanly without
        // touching the transport.
        Assert.Equal(0, failingChannel.Attempts);
    }
}

internal static class GatewayAppFactoryTestExtensions
{
    /// <summary>
    /// Helper that defers service overrides into the test host after the
    /// factory has been configured. Mirrors the pattern used by
    /// <see cref="WebApplicationFactoryExtensions"/> but pulled out so test
    /// files can call it directly without fighting generics.
    /// </summary>
    public static GatewayAppFactory ConfigureTestServices(
        this GatewayAppFactory factory, Action<IServiceCollection> configure)
    {
        factory.TestServicesConfigurations.Add(configure);
        return factory;
    }
}
