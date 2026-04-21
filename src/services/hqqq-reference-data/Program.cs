using Hqqq.Infrastructure.Hosting;
using Hqqq.Observability.Hosting;
using Hqqq.ReferenceData.Configuration;
using Hqqq.ReferenceData.Endpoints;
using Hqqq.ReferenceData.Health;
using Hqqq.ReferenceData.Jobs;
using Hqqq.ReferenceData.Publishing;
using Hqqq.ReferenceData.Services;
using Hqqq.ReferenceData.Sources;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddLegacyFlatKeyFallback();

builder.Services.AddHqqqKafka(builder.Configuration);
builder.Services.AddHqqqRedis(builder.Configuration);
builder.Services.AddHqqqTimescale(builder.Configuration);
// Operating mode still participates in cross-cutting concerns (auth posture,
// logging enrichment) but no longer branches basket ownership — reference-data
// owns the active basket in BOTH Hybrid and Standalone modes; the only
// remaining mode-specific concern is ingress (handled elsewhere).
builder.Services.AddHqqqOperatingMode(builder.Configuration);

builder.Services.Configure<ReferenceDataOptions>(
    builder.Configuration.GetSection(ReferenceDataOptions.SectionName));

var healthChecks = builder.Services.AddHqqqObservability("hqqq-reference-data", builder.Environment)
    .AddKafkaHealthCheck();
builder.Services.AddHqqqMetricsExporter();

// Holdings-source pipeline.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<BasketSeedLoader>();
builder.Services.AddSingleton<FallbackSeedHoldingsSource>();
builder.Services.AddHttpClient("hqqq-refdata-live-holdings");
builder.Services.AddSingleton<LiveHoldingsSource>();
builder.Services.AddSingleton<HoldingsValidator>();
builder.Services.AddSingleton<IHoldingsSource, CompositeHoldingsSource>();

// Active-basket lifecycle.
builder.Services.AddSingleton<ActiveBasketStore>();
builder.Services.AddSingleton<PublishHealthTracker>();
// Observable Prometheus gauges for publish health. Instantiated eagerly
// so the gauge callbacks are registered before the first scrape.
builder.Services.AddSingleton<PublishHealthMetrics>();
builder.Services.AddSingleton<BasketRefreshPipeline>();
builder.Services.AddSingleton<IBasketService, BasketService>();
builder.Services.AddSingleton<IBasketPublisher, KafkaBasketPublisher>();
builder.Services.AddHostedService<BasketRefreshJob>();

// Active-basket readiness probe (reports live vs fallback lineage to operators).
healthChecks.Add(new HealthCheckRegistration(
    name: "active-basket",
    factory: sp => ActivatorUtilities.CreateInstance<ActiveBasketHealthCheck>(sp),
    failureStatus: null,
    tags: new[] { ObservabilityRegistration.ReadyTag }));

var app = builder.Build();

app.Services.LogConfigurationPosture(
    "hqqq-reference-data",
    app.Logger,
    "Kafka", "Redis", "Timescale");

// Eagerly instantiate PublishHealthMetrics so the observable gauges are
// wired before the first /metrics scrape.
_ = app.Services.GetRequiredService<PublishHealthMetrics>();

// /healthz/ready must reflect Kafka publish health: Degraded and Unhealthy
// both map to HTTP 503 so Kubernetes-style readiness probes stop routing to
// a service whose downstream basket topic has stalled. ASP.NET's default
// mapping silently returns 200 for Degraded, which would defeat the whole
// state machine in ActiveBasketHealthCheck.
var readyStatusCodes = new Dictionary<HealthStatus, int>
{
    [HealthStatus.Healthy] = StatusCodes.Status200OK,
    [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
    [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
};
app.MapHqqqHealthEndpoints(readyStatusCodes);
app.MapBasketEndpoints();

app.Run();

public partial class Program { }
