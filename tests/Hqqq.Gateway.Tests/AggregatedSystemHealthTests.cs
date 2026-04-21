using System.Net;
using System.Text.Json;
using Hqqq.Gateway.Tests.Fixtures;

namespace Hqqq.Gateway.Tests;

/// <summary>
/// Phase 2D1 — covers the native <c>/api/system/health</c> aggregator:
/// dependency composition, status rollup, and the "degraded-not-crashed"
/// failure posture (a missing or unreachable downstream surfaces in the
/// payload but the gateway still returns 200).
/// </summary>
public class AggregatedSystemHealthTests
{
    private static GatewayAppFactory FactoryWithAllServicesConfigured(
        ScriptedServiceHealthClient client)
        => new GatewayAppFactory()
            .WithConfig("Gateway:DataSource", "stub")
            // Default for D1 is `aggregated`; setting it explicitly keeps the
            // intent obvious to anyone reading the test.
            .WithConfig("Gateway:Sources:SystemHealth", "aggregated")
            .WithConfig("Gateway:Health:RequestTimeoutSeconds", "0.5")
            .WithConfig("Gateway:Health:IncludeRedis", "false")
            .WithConfig("Gateway:Health:IncludeTimescale", "false")
            .WithConfig("Gateway:Health:Services:ReferenceData:BaseUrl", "http://refdata.test")
            .WithConfig("Gateway:Health:Services:Ingress:BaseUrl", "http://ingress.test")
            .WithConfig("Gateway:Health:Services:QuoteEngine:BaseUrl", "http://qe.test")
            .WithConfig("Gateway:Health:Services:Persistence:BaseUrl", "http://persist.test")
            .WithConfig("Gateway:Health:Services:Analytics:BaseUrl", "http://analytics.test")
            .WithFakeServiceHealthClient(client);

    [Fact]
    public async Task AllServicesHealthy_ReturnsHealthy_WithEveryServiceAsDependency()
    {
        var client = new ScriptedServiceHealthClient()
            .SetHealthy("hqqq-reference-data")
            .SetHealthy("hqqq-ingress")
            .SetHealthy("hqqq-quote-engine")
            .SetHealthy("hqqq-persistence")
            .SetHealthy("hqqq-analytics");

        using var factory = FactoryWithAllServicesConfigured(client);
        using var http = factory.CreateClient();

        var response = await http.GetAsync("/api/system/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;

        Assert.Equal("hqqq-gateway", root.GetProperty("serviceName").GetString());
        Assert.Equal("aggregated", root.GetProperty("sourceMode").GetString());
        Assert.Equal("healthy", root.GetProperty("status").GetString());

        var depNames = root.GetProperty("dependencies")
            .EnumerateArray()
            .Select(d => d.GetProperty("name").GetString())
            .ToArray();
        Assert.Equal(5, depNames.Length);
        Assert.Contains("hqqq-reference-data", depNames);
        Assert.Contains("hqqq-ingress", depNames);
        Assert.Contains("hqqq-quote-engine", depNames);
        Assert.Contains("hqqq-persistence", depNames);
        Assert.Contains("hqqq-analytics", depNames);
    }

    [Fact]
    public async Task SingleServiceUnreachable_DoesNotCrashGateway_AndRollsUpAsDegraded()
    {
        var client = new ScriptedServiceHealthClient()
            .SetHealthy("hqqq-reference-data")
            .SetUnreachable("hqqq-ingress", "unreachable: connection refused")
            .SetHealthy("hqqq-quote-engine")
            .SetHealthy("hqqq-persistence")
            .SetHealthy("hqqq-analytics");

        using var factory = FactoryWithAllServicesConfigured(client);
        using var http = factory.CreateClient();

        var response = await http.GetAsync("/api/system/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;

        // hqqq-ingress is architecturally required — a non-healthy status
        // (even `unknown` from an unreachable probe) escalates the top-level
        // rollup to `degraded`. The gateway intentionally refuses to pretend
        // Phase 2 is healthy when a required worker is missing.
        Assert.Equal("degraded", root.GetProperty("status").GetString());

        var byName = root.GetProperty("dependencies")
            .EnumerateArray()
            .ToDictionary(d => d.GetProperty("name").GetString()!);
        Assert.Equal("unknown", byName["hqqq-ingress"].GetProperty("status").GetString());
        Assert.Contains("connection refused",
            byName["hqqq-ingress"].GetProperty("details").GetString()!);
    }

    [Fact]
    public async Task DowngradedDownstream_RollsUpToDegraded()
    {
        var client = new ScriptedServiceHealthClient()
            .SetHealthy("hqqq-reference-data")
            .SetSnapshot("hqqq-ingress", new()
            {
                ServiceName = "hqqq-ingress",
                Status = "degraded",
                UptimeSeconds = 5,
                Dependencies = Array.Empty<Hqqq.Gateway.Services.Adapters.Aggregated.ServiceHealthSnapshot.DependencyEntry>(),
                LastCheckedAtUtc = DateTimeOffset.UtcNow,
            })
            .SetHealthy("hqqq-quote-engine")
            .SetHealthy("hqqq-persistence")
            .SetHealthy("hqqq-analytics");

        using var factory = FactoryWithAllServicesConfigured(client);
        using var http = factory.CreateClient();

        var response = await http.GetAsync("/api/system/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("degraded", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task UnhealthyDownstream_DoesNotCrashGateway_AndRollsUpToDegradedNotUnhealthy()
    {
        // Phase 2D1 contract: a single unhealthy worker collapses to top-level
        // degraded, never unhealthy, so the frontend doesn't render the whole
        // system as down.
        var client = new ScriptedServiceHealthClient()
            .SetHealthy("hqqq-reference-data")
            .SetSnapshot("hqqq-ingress", new()
            {
                ServiceName = "hqqq-ingress",
                Status = "unhealthy",
                UptimeSeconds = 1,
                Dependencies = Array.Empty<Hqqq.Gateway.Services.Adapters.Aggregated.ServiceHealthSnapshot.DependencyEntry>(),
                LastCheckedAtUtc = DateTimeOffset.UtcNow,
            })
            .SetHealthy("hqqq-quote-engine")
            .SetHealthy("hqqq-persistence")
            .SetHealthy("hqqq-analytics");

        using var factory = FactoryWithAllServicesConfigured(client);
        using var http = factory.CreateClient();

        var response = await http.GetAsync("/api/system/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("degraded", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task UnconfiguredServiceBaseUrl_SurfacesAsIdle_NotConfigured()
    {
        var client = new ScriptedServiceHealthClient()
            .SetHealthy("hqqq-ingress")
            .SetHealthy("hqqq-quote-engine")
            .SetHealthy("hqqq-persistence")
            .SetHealthy("hqqq-analytics");

        // Reference-data is intentionally not configured.
        using var factory = new GatewayAppFactory()
            .WithConfig("Gateway:DataSource", "stub")
            .WithConfig("Gateway:Sources:SystemHealth", "aggregated")
            .WithConfig("Gateway:Health:IncludeRedis", "false")
            .WithConfig("Gateway:Health:IncludeTimescale", "false")
            .WithConfig("Gateway:Health:Services:ReferenceData:BaseUrl", "")
            .WithConfig("Gateway:Health:Services:Ingress:BaseUrl", "http://ingress.test")
            .WithConfig("Gateway:Health:Services:QuoteEngine:BaseUrl", "http://qe.test")
            .WithConfig("Gateway:Health:Services:Persistence:BaseUrl", "http://persist.test")
            .WithConfig("Gateway:Health:Services:Analytics:BaseUrl", "http://analytics.test")
            .WithFakeServiceHealthClient(client);
        using var http = factory.CreateClient();

        var response = await http.GetAsync("/api/system/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var byName = doc.RootElement.GetProperty("dependencies")
            .EnumerateArray()
            .ToDictionary(d => d.GetProperty("name").GetString()!);
        Assert.Equal("idle", byName["hqqq-reference-data"].GetProperty("status").GetString());
        Assert.Equal("not configured",
            byName["hqqq-reference-data"].GetProperty("details").GetString());
        // hqqq-reference-data is architecturally required; even `idle` escalates
        // the rollup to `degraded` so operators see the misconfiguration.
        Assert.Equal("degraded", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AggregatedMode_UsesHealthAggregatorHttpClient_NotLegacyClient()
    {
        // Sanity: the aggregated source must NOT trigger any request through
        // the legacy HttpClient even when DataSource=legacy.
        var legacy = new FakeHttpMessageHandler();
        var client = new ScriptedServiceHealthClient()
            .SetHealthy("hqqq-reference-data")
            .SetHealthy("hqqq-ingress")
            .SetHealthy("hqqq-quote-engine")
            .SetHealthy("hqqq-persistence")
            .SetHealthy("hqqq-analytics");

        using var factory = new GatewayAppFactory()
            .WithConfig("Gateway:DataSource", "legacy")
            .WithConfig("Gateway:LegacyBaseUrl", "http://legacy.test")
            // Default would be aggregated anyway; pinning it removes ambiguity.
            .WithConfig("Gateway:Sources:SystemHealth", "aggregated")
            .WithConfig("Gateway:Health:IncludeRedis", "false")
            .WithConfig("Gateway:Health:IncludeTimescale", "false")
            .WithConfig("Gateway:Health:Services:Ingress:BaseUrl", "http://ingress.test")
            .WithConfig("Gateway:Health:Services:QuoteEngine:BaseUrl", "http://qe.test")
            .WithConfig("Gateway:Health:Services:Persistence:BaseUrl", "http://persist.test")
            .WithConfig("Gateway:Health:Services:Analytics:BaseUrl", "http://analytics.test")
            .WithConfig("Gateway:Health:Services:ReferenceData:BaseUrl", "http://refdata.test")
            .WithFakeHandler(legacy)
            .WithFakeServiceHealthClient(client);
        using var http = factory.CreateClient();

        var response = await http.GetAsync("/api/system/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.DoesNotContain(legacy.Requests,
            r => r.RequestUri!.AbsolutePath == "/api/system/health");
    }
}
