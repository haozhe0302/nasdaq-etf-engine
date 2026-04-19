using Hqqq.Observability.Health;
using Hqqq.Observability.Identity;
using Hqqq.Observability.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;

namespace Hqqq.Observability.Hosting;

/// <summary>
/// Registration entry points for the Phase 2 shared observability layer:
/// service identity, <see cref="HqqqMetrics"/>, OpenTelemetry MeterProvider
/// with the Prometheus exporter, and the standard <c>/healthz/*</c> +
/// <c>/metrics</c> endpoint mappings.
/// </summary>
public static class ObservabilityRegistration
{
    /// <summary>
    /// Tag applied to health checks that should run on <c>/healthz/live</c>
    /// (process-only checks, no external dependencies). Untagged checks
    /// surface only on <c>/healthz/ready</c>.
    /// </summary>
    public const string LiveTag = "live";

    /// <summary>
    /// Tag applied to dependency health checks that should run on
    /// <c>/healthz/ready</c>. Applied automatically by the infrastructure
    /// health-check extensions in Hqqq.Infrastructure.
    /// </summary>
    public const string ReadyTag = "ready";

    /// <summary>
    /// Registers the shared observability layer (no Prometheus exporter):
    /// <list type="bullet">
    ///   <item><see cref="ServiceIdentity"/> singleton (captured now).</item>
    ///   <item><see cref="HqqqMetrics"/> singleton.</item>
    ///   <item>An <c>AddHealthChecks().AddCheck("self", Healthy, [live])</c>
    ///         baseline so <c>/healthz/live</c> always responds.</item>
    /// </list>
    /// Web services should additionally call
    /// <see cref="AddHqqqMetricsExporter"/> so that <c>/metrics</c> works on
    /// the same WebApplication. Workers leave the exporter to the embedded
    /// <see cref="ManagementHost"/> via <c>AddHqqqManagementHost</c>.
    /// </summary>
    public static IHealthChecksBuilder AddHqqqObservability(
        this IServiceCollection services,
        string serviceName,
        IHostEnvironment environment)
    {
        var identity = ServiceIdentity.Capture(serviceName, environment);
        services.AddSingleton(identity);
        services.AddSingleton<HqqqMetrics>();

        return services.AddHealthChecks()
            .AddCheck(
                "self",
                () => HealthCheckResult.Healthy("Process is running"),
                tags: new[] { LiveTag });
    }

    /// <summary>
    /// Registers the OpenTelemetry MeterProvider with the <c>Hqqq</c> meter
    /// and the Prometheus AspNetCore exporter. Call on the
    /// <see cref="IServiceCollection"/> that backs whichever ASP.NET Core
    /// application will expose the <c>/metrics</c> endpoint via
    /// <see cref="MapHqqqHealthEndpoints"/>.
    /// </summary>
    public static IServiceCollection AddHqqqMetricsExporter(this IServiceCollection services)
    {
        services.AddOpenTelemetry()
            .WithMetrics(b => b
                .AddMeter(MetricNames.MeterName)
                .AddPrometheusExporter());
        return services;
    }

    /// <summary>
    /// Maps the standard set of management endpoints on the given application:
    /// <c>GET /healthz/live</c>, <c>GET /healthz/ready</c>, and the
    /// Prometheus scrape endpoint at <c>GET /metrics</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapHqqqHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/healthz/live", new()
        {
            Predicate = check => check.Tags.Contains(LiveTag),
            ResponseWriter = WriteHealthzResponse,
        });

        app.MapHealthChecks("/healthz/ready", new()
        {
            ResponseWriter = WriteHealthzResponse,
        });

        app.MapPrometheusScrapingEndpoint("/metrics");
        return app;
    }

    private static Task WriteHealthzResponse(
        HttpContext context, HealthReport report)
    {
        var identity = context.RequestServices.GetRequiredService<ServiceIdentity>();
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(HealthzPayloadBuilder.Build(identity, report));
    }
}
