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

    /// <summary>
    /// Phase 2 services that are architecturally required for the
    /// system-health rollup. When either is non-healthy (including
    /// <c>idle</c>/<c>unknown</c>) the top-level status escalates to
    /// <c>degraded</c>, so the gateway surfaces operator-visible
    /// misconfiguration instead of pretending the stack is fine.
    /// </summary>
    private static readonly HashSet<string> RequiredServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "hqqq-ingress",
        "hqqq-reference-data",
    };

    /// <summary>
    /// Configuration values that explicitly mark a downstream service as
    /// "not deployed in this environment". Treated identically to a
    /// missing/empty <c>BaseUrl</c>: the aggregator emits the dependency
    /// as <c>idle</c> / "not configured" and skips the HTTP probe entirely.
    /// Lets operators wire optional Phase 2 components (notably
    /// hqqq-analytics, which runs as a job rather than a long-lived HTTP
    /// service) without producing spurious <c>unknown</c> "invalid base
    /// url" rows in /api/system/health.
    /// </summary>
    private static readonly HashSet<string> IdleBaseUrlSentinels = new(StringComparer.OrdinalIgnoreCase)
    {
        "idle",
        "disabled",
        "none",
        "not configured",
    };

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
        var ingressSnapshots = new List<ServiceHealthSnapshot>();
        for (int i = 0; i < serviceEntries.Length; i++)
        {
            dependencies.Add(serviceEntries[i].Entry);
            if (string.Equals(serviceEntries[i].Entry.Name, "hqqq-ingress", StringComparison.OrdinalIgnoreCase)
                && serviceEntries[i].Snapshot is { } snap)
            {
                ingressSnapshots.Add(snap);
            }
        }

        if (localReport is not null)
        {
            foreach (var entry in localReport.Entries)
            {
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

        var topLevel = ComputeTopLevelStatus(dependencies);
        var upstream = BuildUpstreamView(ingressSnapshots);
        var json = SystemHealthPayloadBuilder.Build(_identity, topLevel, dependencies, upstream);
        return Results.Content(json, "application/json", Encoding.UTF8, statusCode: 200);
    }

    /// <summary>
    /// Projects the ingress probe's <c>tiingo-upstream</c> dependency data
    /// dict into the system-health <c>upstream</c> block. Returns
    /// <c>null</c> when ingress is unreachable or does not advertise the
    /// structured fields (older builds) so the frontend gracefully renders
    /// the upstream tile in its default state.
    /// </summary>
    private static SystemHealthPayloadBuilder.UpstreamView? BuildUpstreamView(
        IReadOnlyList<ServiceHealthSnapshot> ingressSnapshots)
    {
        foreach (var snap in ingressSnapshots)
        {
            if (snap.Error is not null) continue;
            foreach (var dep in snap.Dependencies)
            {
                if (!string.Equals(dep.Name, "tiingo-upstream", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (dep.Data is null) continue;

                var ws = ReadBool(dep.Data, "webSocketConnected", "isUpstreamConnected") ?? false;
                var fallback = ReadBool(dep.Data, "fallbackActive") ?? false;
                var lastErr = ReadString(dep.Data, "lastError");
                var lastErrAt = ReadDateTimeOffset(dep.Data, "lastErrorAtUtc");
                var lastPub = ReadDateTimeOffset(dep.Data, "lastPublishedTickUtc");

                return new SystemHealthPayloadBuilder.UpstreamView(
                    WebSocketConnected: ws,
                    FallbackActive: fallback,
                    LastUpstreamError: lastErr,
                    LastUpstreamErrorAtUtc: lastErrAt,
                    LastPublishedTickUtc: lastPub);
            }
        }
        return null;
    }

    private static bool? ReadBool(IReadOnlyDictionary<string, object?> data, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (data.TryGetValue(key, out var value) && value is bool b) return b;
        }
        return null;
    }

    private static string? ReadString(IReadOnlyDictionary<string, object?> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null) return null;
        return value as string ?? value.ToString();
    }

    private static DateTimeOffset? ReadDateTimeOffset(IReadOnlyDictionary<string, object?> data, string key)
    {
        if (!data.TryGetValue(key, out var value) || value is null) return null;
        if (value is DateTimeOffset dt) return dt;
        if (value is DateTime dtUtc) return new DateTimeOffset(dtUtc, TimeSpan.Zero);
        if (value is string s && DateTimeOffset.TryParse(s, out var parsed)) return parsed;
        return null;
    }

    /// <summary>
    /// The Phase 2 services hqqq-ingress and hqqq-reference-data are
    /// architecturally required — the rollup escalates to <c>degraded</c>
    /// when either is not <c>healthy</c>. For every other dependency we
    /// defer to the permissive <see cref="SystemHealthPayloadBuilder.RollupStatus"/>
    /// (unhealthy/degraded escalate; idle/unknown stay silent).
    /// </summary>
    private static string ComputeTopLevelStatus(IReadOnlyList<SystemHealthPayloadBuilder.DependencyEntry> dependencies)
    {
        var rollup = SystemHealthPayloadBuilder.RollupStatus(dependencies.Select(d => d.Status));

        foreach (var d in dependencies)
        {
            if (!RequiredServices.Contains(d.Name)) continue;
            if (d.Status != SystemHealthPayloadBuilder.Status.Healthy)
            {
                return SystemHealthPayloadBuilder.Status.Degraded;
            }
        }
        return rollup;
    }

    /// <summary>
    /// Result of one downstream probe: the rolled-up dependency entry and
    /// (when reachable) the raw <see cref="ServiceHealthSnapshot"/> so the
    /// caller can pull additional fields out — e.g. the ingress
    /// <c>tiingo-upstream</c> data dict for the system-health
    /// <c>upstream</c> block.
    /// </summary>
    private readonly record struct ProbeResult(
        SystemHealthPayloadBuilder.DependencyEntry Entry,
        ServiceHealthSnapshot? Snapshot);

    private async Task<ProbeResult> ProbeServiceAsync(
        string key, string serviceName, CancellationToken ct)
    {
        _options.Services.TryGetValue(key, out var endpoint);
        var rawBaseUrl = endpoint?.BaseUrl?.Trim();

        // Missing/empty BaseUrl, or one of the documented idle sentinels
        // (idle/disabled/none/"not configured") means the operator has
        // intentionally opted the service out in this environment. Skip
        // the HTTP probe and surface a stable idle row instead of letting
        // the URL parser produce an "unknown / invalid base url" entry.
        if (string.IsNullOrWhiteSpace(rawBaseUrl) || IdleBaseUrlSentinels.Contains(rawBaseUrl))
        {
            // hqqq-analytics is a one-shot/optional job, not a required
            // long-running service. Make the idle label explicit so the UI
            // does not read it as a missing dependency.
            var details = string.Equals(serviceName, "hqqq-analytics", StringComparison.OrdinalIgnoreCase)
                ? "Optional analytics job \u2014 not configured"
                : "not configured";

            return new ProbeResult(
                new SystemHealthPayloadBuilder.DependencyEntry(
                    Name: serviceName,
                    Status: SystemHealthPayloadBuilder.Status.Idle,
                    LastCheckedAtUtc: DateTimeOffset.UtcNow,
                    Details: details),
                null);
        }

        if (!Uri.TryCreate(rawBaseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
        {
            return new ProbeResult(
                new SystemHealthPayloadBuilder.DependencyEntry(
                    Name: serviceName,
                    Status: SystemHealthPayloadBuilder.Status.Unknown,
                    LastCheckedAtUtc: DateTimeOffset.UtcNow,
                    Details: $"invalid base url: {rawBaseUrl}"),
                null);
        }

        var snapshot = await _client.ProbeAsync(serviceName, baseUri, ct).ConfigureAwait(false);
        return new ProbeResult(BuildServiceEntry(serviceName, snapshot), snapshot);
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
