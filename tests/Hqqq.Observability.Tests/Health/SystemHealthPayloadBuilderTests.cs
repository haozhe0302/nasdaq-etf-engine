using System.Text.Json;
using Hqqq.Observability.Health;
using Hqqq.Observability.Identity;

namespace Hqqq.Observability.Tests.Health;

/// <summary>
/// Locks the gateway-facing <c>/api/system/health</c> shape and the rollup
/// rule. These contracts are consumed directly by the frontend and the
/// gateway aggregator, so any drift here is a frontend-visible regression.
/// </summary>
public class SystemHealthPayloadBuilderTests
{
    private static ServiceIdentity Identity() => new()
    {
        ServiceName = "hqqq-gateway",
        ServiceVersion = "1.2.3",
        Environment = "Production",
        StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
        MachineName = "host-a",
    };

    [Fact]
    public void Build_EmitsBSystemHealthShape_WithSourceModeAggregated()
    {
        var deps = new List<SystemHealthPayloadBuilder.DependencyEntry>
        {
            new("hqqq-ingress", SystemHealthPayloadBuilder.Status.Healthy,
                DateTimeOffset.UtcNow, "uptime=12s"),
            new("redis", SystemHealthPayloadBuilder.Status.Degraded,
                DateTimeOffset.UtcNow, "latency=200ms"),
        };

        var json = SystemHealthPayloadBuilder.Build(
            Identity(), SystemHealthPayloadBuilder.Status.Degraded, deps);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("hqqq-gateway", root.GetProperty("serviceName").GetString());
        Assert.Equal("degraded", root.GetProperty("status").GetString());
        Assert.Equal("1.2.3", root.GetProperty("version").GetString());
        Assert.Equal("aggregated", root.GetProperty("sourceMode").GetString());
        Assert.True(root.TryGetProperty("runtime", out var runtime));
        Assert.True(runtime.GetProperty("uptimeSeconds").GetInt64() >= 0);

        var depArr = root.GetProperty("dependencies");
        Assert.Equal(2, depArr.GetArrayLength());
        Assert.Equal("hqqq-ingress", depArr[0].GetProperty("name").GetString());
        Assert.Equal("healthy", depArr[0].GetProperty("status").GetString());
        Assert.Equal("redis", depArr[1].GetProperty("name").GetString());
        Assert.Equal("degraded", depArr[1].GetProperty("status").GetString());
        Assert.Equal("latency=200ms", depArr[1].GetProperty("details").GetString());
    }

    [Fact]
    public void Build_EmitsUpstreamBlock_WhenSupplied()
    {
        // Phase 2 ingress fallback contract: when the gateway has resolved
        // structured upstream state from the ingress probe, it surfaces it
        // here so the frontend /system page renders Fallback Active even
        // though the websocket is down.
        var deps = new List<SystemHealthPayloadBuilder.DependencyEntry>
        {
            new("hqqq-ingress", SystemHealthPayloadBuilder.Status.Degraded,
                DateTimeOffset.UtcNow, "uptime=30s"),
        };
        var lastPub = DateTimeOffset.UtcNow.AddSeconds(-5);
        var upstream = new SystemHealthPayloadBuilder.UpstreamView(
            WebSocketConnected: false,
            FallbackActive: true,
            LastUpstreamError: "ws closed by server",
            LastUpstreamErrorAtUtc: lastPub,
            LastPublishedTickUtc: lastPub);

        var json = SystemHealthPayloadBuilder.Build(
            Identity(), SystemHealthPayloadBuilder.Status.Degraded, deps, upstream);

        using var doc = JsonDocument.Parse(json);
        var up = doc.RootElement.GetProperty("upstream");
        Assert.False(up.GetProperty("webSocketConnected").GetBoolean());
        Assert.True(up.GetProperty("fallbackActive").GetBoolean());
        Assert.Equal("ws closed by server", up.GetProperty("lastUpstreamError").GetString());
        Assert.True(up.TryGetProperty("lastPublishedTickUtc", out _));
    }

    [Fact]
    public void Build_OmitsUpstreamBlock_WhenNotSupplied()
    {
        // Backwards-compatible default — no `upstream` field at all, not
        // a null one, so the previous payload bytes are preserved for
        // services that don't have upstream state to surface.
        var deps = new List<SystemHealthPayloadBuilder.DependencyEntry>
        {
            new("hqqq-ingress", SystemHealthPayloadBuilder.Status.Healthy,
                DateTimeOffset.UtcNow, "uptime=30s"),
        };

        var json = SystemHealthPayloadBuilder.Build(
            Identity(), SystemHealthPayloadBuilder.Status.Healthy, deps);

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.TryGetProperty("upstream", out _));
    }

    [Theory]
    [InlineData(new[] { "healthy", "healthy" }, "healthy")]
    [InlineData(new[] { "healthy", "unknown" }, "healthy")]
    [InlineData(new[] { "healthy", "idle" }, "healthy")]
    [InlineData(new[] { "healthy", "degraded" }, "degraded")]
    // Phase 2D1 — top-level collapses unhealthy to degraded so a single
    // missing worker doesn't trip frontend alarms.
    [InlineData(new[] { "healthy", "unhealthy" }, "degraded")]
    [InlineData(new[] { "unhealthy", "unhealthy" }, "degraded")]
    [InlineData(new string[0], "healthy")]
    public void RollupStatus_AppliesDegradedNotCrashedRule(string[] statuses, string expected)
    {
        Assert.Equal(expected, SystemHealthPayloadBuilder.RollupStatus(statuses));
    }
}
