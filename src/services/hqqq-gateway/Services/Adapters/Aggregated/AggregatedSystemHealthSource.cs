using System.Globalization;
using System.Text;
using Hqqq.Gateway.Services.Sources;
using Hqqq.Observability.Health;
using Hqqq.Observability.Identity;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Hqqq.Gateway.Services.Adapters.Aggregated;

/// <summary>
/// Native gateway aggregator for <c>/api/system/health</c>. In parallel:
/// <list type="bullet">
///   <item>Probes every configured downstream service's <c>/healthz/ready</c>
///         via <see cref="IServiceHealthClient"/>.</item>
///   <item>Runs the local in-process <see cref="HealthCheckService"/> so the
///         Redis / Timescale dependency probes that the gateway itself uses
///         appear in the aggregated payload too.</item>
/// </list>
/// Composes the result into a <c>BSystemHealth</c>-shaped JSON via
/// <see cref="SystemHealthPayloadBuilder"/> so the existing frontend adapter
/// keeps rendering without any change.
/// Always returns HTTP 200 with the payload; no exception bubbles, no
/// silent fallback to legacy/stub.
/// </summary>
public sealed class AggregatedSystemHealthSource : ISystemHealthSource
{
    private readonly IServiceHealthClient _client;
    private readonly HealthCheckService _localHealth;
    private readonly ServiceIdentity _identity;
    private readonly GatewayHealthOptions _options;
    private readonly ILogger<AggregatedSystemHealthSource> _logger;

    public AggregatedSystemHealthSource(
        IServiceHealthClient client,
        HealthCheckService localHealth,
        ServiceIdentity identity,
        IOptions<GatewayHealthOptions> options,
        ILogger<AggregatedSystemHealthSource> logger)
    {
        _client = client;
        _localHealth = localHealth;
        _identity = identity;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IResult> GetSystemHealthAsync(CancellationToken ct)
    {
        var dependencies = new List<SystemHealthPayloadBuilder.DependencyEntry>();

        var serviceTasks = GatewayHealthOptions.KnownServices
            .Select(svc => ProbeServiceAsync(svc.Key, svc.ServiceName, ct))
            .ToArray();

        HealthReport? localReport = null;
        try
        {
            localReport = await _localHealth.CheckHealthAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Local HealthCheckService failed during aggregation");
        }

        var serviceEntries = await Task.WhenAll(serviceTasks).ConfigureAwait(false);
        foreach (var entry in serviceEntries)
            dependencies.Add(entry);

        if (localReport is not null)
        {
            foreach (var entry in localReport.Entries)
            {
                // "self" is a process-only check that should not surface as a
                // dependency in the aggregated view (the runtime block carries
                // local liveness already).
                if (string.Equals(entry.Key, "self", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (entry.Key == "redis" && !_options.IncludeRedis) continue;
                if (entry.Key == "timescale" && !_options.IncludeTimescale) continue;

                dependencies.Add(new SystemHealthPayloadBuilder.DependencyEntry(
                    Name: entry.Key,
                    Status: HealthzPayloadBuilder.MapStatus(entry.Value.Status),
                    LastCheckedAtUtc: DateTimeOffset.UtcNow,
                    Details: BuildLocalDetails(entry.Value)));
            }
        }

        var topLevel = SystemHealthPayloadBuilder.RollupStatus(dependencies.Select(d => d.Status));
        var json = SystemHealthPayloadBuilder.Build(_identity, topLevel, dependencies);
        return Results.Content(json, "application/json", Encoding.UTF8, statusCode: 200);
    }

    private async Task<SystemHealthPayloadBuilder.DependencyEntry> ProbeServiceAsync(
        string key, string serviceName, CancellationToken ct)
    {
        if (!_options.Services.TryGetValue(key, out var endpoint)
            || string.IsNullOrWhiteSpace(endpoint?.BaseUrl))
        {
            return new SystemHealthPayloadBuilder.DependencyEntry(
                Name: serviceName,
                Status: SystemHealthPayloadBuilder.Status.Idle,
                LastCheckedAtUtc: DateTimeOffset.UtcNow,
                Details: "not configured");
        }

        if (!Uri.TryCreate(endpoint.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            return new SystemHealthPayloadBuilder.DependencyEntry(
                Name: serviceName,
                Status: SystemHealthPayloadBuilder.Status.Unknown,
                LastCheckedAtUtc: DateTimeOffset.UtcNow,
                Details: $"invalid base url: {endpoint.BaseUrl}");
        }

        var snapshot = await _client.ProbeAsync(serviceName, baseUri, ct).ConfigureAwait(false);
        return BuildServiceEntry(serviceName, snapshot);
    }

    private static SystemHealthPayloadBuilder.DependencyEntry BuildServiceEntry(
        string serviceName, ServiceHealthSnapshot snapshot)
    {
        if (snapshot.Error is not null)
        {
            return new SystemHealthPayloadBuilder.DependencyEntry(
                Name: serviceName,
                Status: SystemHealthPayloadBuilder.Status.Unknown,
                LastCheckedAtUtc: snapshot.LastCheckedAtUtc,
                Details: snapshot.Error);
        }

        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(snapshot.Version))
            sb.Append("version=").Append(snapshot.Version);
        if (snapshot.UptimeSeconds.HasValue)
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append("uptime=").Append(snapshot.UptimeSeconds.Value.ToString(CultureInfo.InvariantCulture)).Append('s');
        }
        if (snapshot.Dependencies.Count > 0)
        {
            if (sb.Length > 0) sb.Append(", ");
            var degraded = snapshot.Dependencies
                .Where(d => d.Status is "unhealthy" or "degraded")
                .Select(d => d.Name)
                .ToArray();
            sb.Append("deps=").Append(degraded.Length == 0 ? "ok" : $"degraded({string.Join(',', degraded)})");
        }

        return new SystemHealthPayloadBuilder.DependencyEntry(
            Name: serviceName,
            Status: NormalizeStatus(snapshot.Status),
            LastCheckedAtUtc: snapshot.LastCheckedAtUtc,
            Details: sb.Length == 0 ? null : sb.ToString());
    }

    private static string BuildLocalDetails(HealthReportEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.Description)) return entry.Description!;
        if (entry.Exception is not null) return entry.Exception.Message;
        return $"latency={entry.Duration.TotalMilliseconds:F0}ms";
    }

    private static string NormalizeStatus(string raw) => raw?.ToLowerInvariant() switch
    {
        "healthy" => SystemHealthPayloadBuilder.Status.Healthy,
        "degraded" => SystemHealthPayloadBuilder.Status.Degraded,
        "unhealthy" => SystemHealthPayloadBuilder.Status.Unhealthy,
        "idle" => SystemHealthPayloadBuilder.Status.Idle,
        _ => SystemHealthPayloadBuilder.Status.Unknown,
    };
}
