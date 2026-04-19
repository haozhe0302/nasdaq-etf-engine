using System.Text.Json;
using Hqqq.Observability.Health;
using Hqqq.Observability.Identity;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Hqqq.Observability.Tests.Health;

/// <summary>
/// Locks the JSON shape of the per-service <c>/healthz/*</c> payload that
/// every Phase 2 service emits. The gateway's aggregator depends on these
/// fields, and the frontend never sees this payload directly — keeping the
/// shape stable means downstream rollups don't break silently.
/// </summary>
public class HealthzPayloadBuilderTests
{
    private static ServiceIdentity Identity() => new()
    {
        ServiceName = "hqqq-test",
        ServiceVersion = "9.9.9",
        Environment = "Development",
        StartedAtUtc = DateTimeOffset.UtcNow.AddSeconds(-30),
        MachineName = "test-host",
    };

    [Fact]
    public void Build_EmitsServiceIdentityAndStatus()
    {
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>(),
            HealthStatus.Healthy,
            TimeSpan.FromMilliseconds(5));

        var json = HealthzPayloadBuilder.Build(Identity(), report);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("hqqq-test", root.GetProperty("serviceName").GetString());
        Assert.Equal("9.9.9", root.GetProperty("serviceVersion").GetString());
        Assert.Equal("Development", root.GetProperty("environment").GetString());
        Assert.Equal("test-host", root.GetProperty("machineName").GetString());
        Assert.Equal("healthy", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("uptimeSeconds").GetInt64() >= 0);
        Assert.True(root.TryGetProperty("runtime", out _));
        Assert.Equal(0, root.GetProperty("dependencies").GetArrayLength());
    }

    [Fact]
    public void Build_ProjectsEachDependencyEntry_WithStatusAndDuration()
    {
        var entries = new Dictionary<string, HealthReportEntry>
        {
            ["redis"] = new(
                HealthStatus.Degraded,
                "redis lagging",
                TimeSpan.FromMilliseconds(42),
                exception: null,
                data: null,
                tags: new[] { "ready" }),
            ["kafka"] = new(
                HealthStatus.Healthy,
                "ok",
                TimeSpan.FromMilliseconds(7),
                exception: null,
                data: null,
                tags: new[] { "ready" }),
        };
        var report = new HealthReport(entries, HealthStatus.Degraded, TimeSpan.FromMilliseconds(50));

        var json = HealthzPayloadBuilder.Build(Identity(), report);

        using var doc = JsonDocument.Parse(json);
        var deps = doc.RootElement.GetProperty("dependencies");
        Assert.Equal(2, deps.GetArrayLength());

        var byName = deps.EnumerateArray().ToDictionary(e => e.GetProperty("name").GetString()!);
        Assert.Equal("degraded", byName["redis"].GetProperty("status").GetString());
        Assert.Equal("redis lagging", byName["redis"].GetProperty("description").GetString());
        Assert.Equal(42, byName["redis"].GetProperty("durationMs").GetDouble());
        Assert.Equal("healthy", byName["kafka"].GetProperty("status").GetString());
    }

    [Fact]
    public void MapStatus_CoversAllVocabulary()
    {
        Assert.Equal("healthy", HealthzPayloadBuilder.MapStatus(HealthStatus.Healthy));
        Assert.Equal("degraded", HealthzPayloadBuilder.MapStatus(HealthStatus.Degraded));
        Assert.Equal("unhealthy", HealthzPayloadBuilder.MapStatus(HealthStatus.Unhealthy));
    }
}
