using System.Net;
using System.Text.Json;
using Hqqq.Gateway.Tests.Fixtures;

namespace Hqqq.Gateway.Tests;

/// <summary>
/// Phase 2D1 — frontend contract preservation. The frontend's
/// <c>BSystemHealth</c> adapter reads <c>serviceName</c>, <c>status</c>,
/// <c>checkedAtUtc</c>, <c>version</c>, <c>runtime</c>, optional
/// <c>upstream</c>, and <c>dependencies[]</c> with <c>name</c> +
/// <c>status</c>. The native aggregator must keep emitting all those
/// fields so no frontend change is required for this cutover.
/// </summary>
public class SystemHealthContractTests
{
    [Fact]
    public async Task Aggregated_Payload_PreservesFrontendBSystemHealthShape()
    {
        var client = new ScriptedServiceHealthClient()
            .SetHealthy("hqqq-reference-data")
            .SetHealthy("hqqq-ingress")
            .SetHealthy("hqqq-quote-engine")
            .SetHealthy("hqqq-persistence")
            .SetHealthy("hqqq-analytics");

        using var factory = new GatewayAppFactory()
            .WithConfig("Gateway:DataSource", "stub")
            .WithConfig("Gateway:Sources:SystemHealth", "aggregated")
            .WithConfig("Gateway:Health:IncludeRedis", "false")
            .WithConfig("Gateway:Health:IncludeTimescale", "false")
            .WithConfig("Gateway:Health:Services:ReferenceData:BaseUrl", "http://refdata.test")
            .WithConfig("Gateway:Health:Services:Ingress:BaseUrl", "http://ingress.test")
            .WithConfig("Gateway:Health:Services:QuoteEngine:BaseUrl", "http://qe.test")
            .WithConfig("Gateway:Health:Services:Persistence:BaseUrl", "http://persist.test")
            .WithConfig("Gateway:Health:Services:Analytics:BaseUrl", "http://analytics.test")
            .WithFakeServiceHealthClient(client);
        using var http = factory.CreateClient();

        var response = await http.GetAsync("/api/system/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;

        // BSystemHealth required fields.
        Assert.True(root.TryGetProperty("serviceName", out _));
        Assert.True(root.TryGetProperty("status", out _));
        Assert.True(root.TryGetProperty("checkedAtUtc", out var checkedAt));
        Assert.True(checkedAt.TryGetDateTimeOffset(out _));
        Assert.True(root.TryGetProperty("version", out _));
        Assert.True(root.TryGetProperty("runtime", out var runtime));
        Assert.True(runtime.TryGetProperty("uptimeSeconds", out _));
        Assert.True(runtime.TryGetProperty("memoryMb", out _));

        // Status vocabulary recognized by the frontend's toHealthStatus.
        var status = root.GetProperty("status").GetString();
        Assert.Contains(status, new[] { "healthy", "degraded", "unhealthy", "unknown", "idle" });

        // Dependencies must be an array, with name + status on every entry.
        var deps = root.GetProperty("dependencies");
        Assert.True(deps.ValueKind == JsonValueKind.Array);
        foreach (var dep in deps.EnumerateArray())
        {
            Assert.False(string.IsNullOrEmpty(dep.GetProperty("name").GetString()));
            Assert.False(string.IsNullOrEmpty(dep.GetProperty("status").GetString()));
        }
    }
}
