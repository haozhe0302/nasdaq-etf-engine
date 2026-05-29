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
    public async Task IngressTiingoUpstreamDependency_ProjectsIntoUpstreamBlock_WithFallbackActive()
    {
        // Operating-in-fallback contract from the ingress side: when the
        // websocket is down but the REST fallback loop is publishing,
        // ingress emits structured `data` on its `tiingo-upstream`
        // dependency. The gateway must project those fields into the
        // `upstream` block of /api/system/health so the frontend /system
        // page can render "Fallback Active".
        var lastError = "ws closed by server";
        var lastPub = DateTimeOffset.UtcNow.AddSeconds(-3);
        var client = new ScriptedServiceHealthClient()
            .SetHealthy("hqqq-reference-data")
            .SetSnapshot("hqqq-ingress", new Hqqq.Gateway.Services.Adapters.Aggregated.ServiceHealthSnapshot
            {
                ServiceName = "hqqq-ingress",
                Status = "degraded",
                UptimeSeconds = 30,
                Dependencies = new[]
                {
                    new Hqqq.Gateway.Services.Adapters.Aggregated.ServiceHealthSnapshot.DependencyEntry(
                        Name: "tiingo-upstream",
                        Status: "degraded",
                        Data: new Dictionary<string, object?>(StringComparer.Ordinal)
                        {
                            ["webSocketConnected"] = false,
                            ["fallbackActive"] = true,
                            ["lastError"] = lastError,
                            ["lastPublishedTickUtc"] = lastPub,
                        }),
                },
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
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("upstream", out var upstream),
            "expected upstream block to be populated from ingress probe");
        Assert.Equal(JsonValueKind.Object, upstream.ValueKind);
        Assert.False(upstream.GetProperty("webSocketConnected").GetBoolean());
        Assert.True(upstream.GetProperty("fallbackActive").GetBoolean());
        Assert.Equal(lastError, upstream.GetProperty("lastUpstreamError").GetString());
        Assert.True(upstream.TryGetProperty("lastPublishedTickUtc", out _));
    }

    [Fact]
    public async Task IngressTiingoUpstreamDependency_WithoutDataDict_LeavesUpstreamNull()
    {
        // Older ingress builds (or any service that doesn't populate the
        // structured data dict) must not break aggregation — the upstream
        // block is simply omitted and the frontend falls back to defaults.
        var client = new ScriptedServiceHealthClient()
            .SetHealthy("hqqq-reference-data")
            .SetSnapshot("hqqq-ingress", new Hqqq.Gateway.Services.Adapters.Aggregated.ServiceHealthSnapshot
            {
                ServiceName = "hqqq-ingress",
                Status = "healthy",
                UptimeSeconds = 30,
                Dependencies = new[]
                {
                    new Hqqq.Gateway.Services.Adapters.Aggregated.ServiceHealthSnapshot.DependencyEntry(
                        Name: "tiingo-upstream",
                        Status: "healthy",
                        Data: null),
                },
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
        var root = doc.RootElement;

        // `upstream` is serialized as null and dropped by WhenWritingNull,
        // so it must not appear in the payload at all.
        Assert.False(root.TryGetProperty("upstream", out _));
    }

    [Theory]
    [InlineData("idle")]
    [InlineData("IDLE")]
    [InlineData("disabled")]
    [InlineData("none")]
    [InlineData("not configured")]
    [InlineData("  idle  ")]
    public async Task AnalyticsBaseUrl_IdleSentinel_SurfacesAsIdleNotConfigured_AndDoesNotDegradeOverall(
        string sentinel)
    {
        // hqqq-analytics is a job / optional component in Phase 2 — it is
        // NOT in RequiredServices. Setting Gateway:Health:Services:Analytics:BaseUrl
        // to one of the documented idle sentinels must:
        //   1. Skip the HTTP probe entirely (no ProbeAsync call recorded).
        //   2. Surface as `idle` / "not configured" in dependencies[].
        //   3. Leave the top-level rollup `healthy` (idle never escalates).
        var client = new ScriptedServiceHealthClient()
            .SetHealthy("hqqq-reference-data")
            .SetHealthy("hqqq-ingress")
            .SetHealthy("hqqq-quote-engine")
            .SetHealthy("hqqq-persistence");

        using var factory = new GatewayAppFactory()
            .WithConfig("Gateway:DataSource", "stub")
            .WithConfig("Gateway:Sources:SystemHealth", "aggregated")
            .WithConfig("Gateway:Health:IncludeRedis", "false")
            .WithConfig("Gateway:Health:IncludeTimescale", "false")
            .WithConfig("Gateway:Health:Services:ReferenceData:BaseUrl", "http://refdata.test")
            .WithConfig("Gateway:Health:Services:Ingress:BaseUrl", "http://ingress.test")
            .WithConfig("Gateway:Health:Services:QuoteEngine:BaseUrl", "http://qe.test")
            .WithConfig("Gateway:Health:Services:Persistence:BaseUrl", "http://persist.test")
            .WithConfig("Gateway:Health:Services:Analytics:BaseUrl", sentinel)
            .WithFakeServiceHealthClient(client);
        using var http = factory.CreateClient();

        var response = await http.GetAsync("/api/system/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;

        var byName = root.GetProperty("dependencies")
            .EnumerateArray()
            .ToDictionary(d => d.GetProperty("name").GetString()!);
        Assert.Equal("idle", byName["hqqq-analytics"].GetProperty("status").GetString());
        Assert.Equal("Optional analytics job \u2014 not configured",
            byName["hqqq-analytics"].GetProperty("details").GetString());

        // Analytics is optional — an idle row must never drag the overall
        // rollup off `healthy`.
        Assert.Equal("healthy", root.GetProperty("status").GetString());

        // And we must not have issued an HTTP probe for analytics.
        Assert.DoesNotContain("hqqq-analytics", client.ProbedServices);
    }

    [Fact]
    public async Task AnalyticsBaseUrl_Empty_SurfacesAsIdle_AndOverallStaysHealthy()
    {
        // hqqq-analytics is non-required, so unlike the reference-data
        // case in UnconfiguredServiceBaseUrl_SurfacesAsIdle_NotConfigured
        // an empty BaseUrl must NOT escalate the rollup — the system
        // stays `healthy`. Operators wire this when running Phase 2
        // without the optional analytics worker.
        var client = new ScriptedServiceHealthClient()
            .SetHealthy("hqqq-reference-data")
            .SetHealthy("hqqq-ingress")
            .SetHealthy("hqqq-quote-engine")
            .SetHealthy("hqqq-persistence");

        using var factory = new GatewayAppFactory()
            .WithConfig("Gateway:DataSource", "stub")
            .WithConfig("Gateway:Sources:SystemHealth", "aggregated")
            .WithConfig("Gateway:Health:IncludeRedis", "false")
            .WithConfig("Gateway:Health:IncludeTimescale", "false")
            .WithConfig("Gateway:Health:Services:ReferenceData:BaseUrl", "http://refdata.test")
            .WithConfig("Gateway:Health:Services:Ingress:BaseUrl", "http://ingress.test")
            .WithConfig("Gateway:Health:Services:QuoteEngine:BaseUrl", "http://qe.test")
            .WithConfig("Gateway:Health:Services:Persistence:BaseUrl", "http://persist.test")
            .WithConfig("Gateway:Health:Services:Analytics:BaseUrl", "")
            .WithFakeServiceHealthClient(client);
        using var http = factory.CreateClient();

        var response = await http.GetAsync("/api/system/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;

        var byName = root.GetProperty("dependencies")
            .EnumerateArray()
            .ToDictionary(d => d.GetProperty("name").GetString()!);
        Assert.Equal("idle", byName["hqqq-analytics"].GetProperty("status").GetString());
        Assert.Equal("Optional analytics job \u2014 not configured",
            byName["hqqq-analytics"].GetProperty("details").GetString());
        Assert.Equal("healthy", root.GetProperty("status").GetString());
        Assert.DoesNotContain("hqqq-analytics", client.ProbedServices);
    }

    [Fact]
    public async Task AnalyticsBaseUrl_ValidHttpUrl_StillHttpProbed()
    {
        // The escape hatch matters in both directions: when a real
        // analytics deployment exists, the operator points Analytics:BaseUrl
        // at it and the aggregator must resume the normal HTTP health
        // check path (probe recorded, snapshot status mapped through).
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
        var byName = doc.RootElement.GetProperty("dependencies")
            .EnumerateArray()
            .ToDictionary(d => d.GetProperty("name").GetString()!);
        Assert.Equal("healthy", byName["hqqq-analytics"].GetProperty("status").GetString());
        Assert.Contains("hqqq-analytics", client.ProbedServices);
    }

    [Fact]
    public async Task AnalyticsBaseUrl_NonsenseValue_StillReportedAsUnknownInvalidBaseUrl()
    {
        // Defensive: an unrecognized non-URL string that isn't one of the
        // sentinels (e.g. a typo) should still surface as `unknown` so the
        // misconfiguration is operator-visible. Analytics is non-required,
        // so this still doesn't escalate the rollup.
        var client = new ScriptedServiceHealthClient()
            .SetHealthy("hqqq-reference-data")
            .SetHealthy("hqqq-ingress")
            .SetHealthy("hqqq-quote-engine")
            .SetHealthy("hqqq-persistence");

        using var factory = new GatewayAppFactory()
            .WithConfig("Gateway:DataSource", "stub")
            .WithConfig("Gateway:Sources:SystemHealth", "aggregated")
            .WithConfig("Gateway:Health:IncludeRedis", "false")
            .WithConfig("Gateway:Health:IncludeTimescale", "false")
            .WithConfig("Gateway:Health:Services:ReferenceData:BaseUrl", "http://refdata.test")
            .WithConfig("Gateway:Health:Services:Ingress:BaseUrl", "http://ingress.test")
            .WithConfig("Gateway:Health:Services:QuoteEngine:BaseUrl", "http://qe.test")
            .WithConfig("Gateway:Health:Services:Persistence:BaseUrl", "http://persist.test")
            .WithConfig("Gateway:Health:Services:Analytics:BaseUrl", "not-a-real-url")
            .WithFakeServiceHealthClient(client);
        using var http = factory.CreateClient();

        var response = await http.GetAsync("/api/system/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;

        var byName = root.GetProperty("dependencies")
            .EnumerateArray()
            .ToDictionary(d => d.GetProperty("name").GetString()!);
        Assert.Equal("unknown", byName["hqqq-analytics"].GetProperty("status").GetString());
        Assert.Contains("invalid base url",
            byName["hqqq-analytics"].GetProperty("details").GetString()!);
        Assert.Equal("healthy", root.GetProperty("status").GetString());
        Assert.DoesNotContain("hqqq-analytics", client.ProbedServices);
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
