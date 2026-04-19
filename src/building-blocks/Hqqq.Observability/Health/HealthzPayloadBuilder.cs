using System.Text.Json;
using System.Text.Json.Serialization;
using Hqqq.Observability.Identity;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Hqqq.Observability.Health;

/// <summary>
/// Serializes the standard machine-readable JSON payload returned by every
/// service's <c>/healthz/live</c> and <c>/healthz/ready</c> endpoints. Output
/// is camelCase, includes service identity (so the gateway aggregator can
/// derive per-service status without a second roundtrip), and reports each
/// dependency check status from the underlying <see cref="HealthReport"/>.
/// </summary>
public static class HealthzPayloadBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Build(ServiceIdentity identity, HealthReport report)
    {
        var runtime = RuntimeInfo.Capture(identity);
        var payload = new
        {
            serviceName = identity.ServiceName,
            serviceVersion = identity.ServiceVersion,
            environment = identity.Environment,
            machineName = identity.MachineName,
            startedAtUtc = identity.StartedAtUtc,
            uptimeSeconds = identity.UptimeSeconds,
            status = MapStatus(report.Status),
            checkedAtUtc = DateTimeOffset.UtcNow,
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            runtime,
            dependencies = report.Entries
                .Select(e => new
                {
                    name = e.Key,
                    status = MapStatus(e.Value.Status),
                    description = e.Value.Description,
                    durationMs = e.Value.Duration.TotalMilliseconds,
                    error = e.Value.Exception?.Message,
                    tags = e.Value.Tags,
                })
                .ToArray(),
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    /// <summary>
    /// Maps an ASP.NET Core <see cref="HealthStatus"/> to the lowercase string
    /// vocabulary used by the frontend and the gateway aggregator.
    /// </summary>
    public static string MapStatus(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => "healthy",
        HealthStatus.Degraded => "degraded",
        HealthStatus.Unhealthy => "unhealthy",
        _ => "unknown",
    };
}
