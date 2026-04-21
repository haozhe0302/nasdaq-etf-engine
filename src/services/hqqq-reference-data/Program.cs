using Hqqq.Infrastructure.Hosting;
using Hqqq.Observability.Hosting;
using Hqqq.ReferenceData.Configuration;
using Hqqq.ReferenceData.CorporateActions.Contracts;
using Hqqq.ReferenceData.CorporateActions.Providers;
using Hqqq.ReferenceData.CorporateActions.Services;
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
// OperatingMode is a logging-posture tag for cross-service consistency;
// reference-data's basket ownership is unconditional in Phase 2 — the
// hybrid/standalone runtime split has been removed from every Phase 2
// service touched by this pass.
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

// Phase-2-native corporate-action pipeline. File provider is
// deterministic and offline-safe; Tiingo overlay is opt-in via
// ReferenceData:CorporateActions:Tiingo:Enabled.
builder.Services.AddHttpClient(TiingoCorporateActionProvider.HttpClientName);
builder.Services.AddSingleton<FileCorporateActionProvider>();
builder.Services.AddSingleton<TiingoCorporateActionProvider>();
builder.Services.AddSingleton<ICorporateActionProvider, CompositeCorporateActionProvider>();
builder.Services.AddSingleton<CorporateActionAdjustmentService>();
builder.Services.AddSingleton<BasketTransitionPlanner>();

// Active-basket lifecycle.
builder.Services.AddSingleton<ActiveBasketStore>();
builder.Services.AddSingleton<PublishHealthTracker>();
builder.Services.AddSingleton<PublishHealthMetrics>();
builder.Services.AddSingleton<BasketRefreshPipeline>();
builder.Services.AddSingleton<IBasketService, BasketService>();
builder.Services.AddSingleton<IBasketPublisher, KafkaBasketPublisher>();
builder.Services.AddHostedService<BasketRefreshJob>();

healthChecks.Add(new HealthCheckRegistration(
    name: "active-basket",
    factory: sp => ActivatorUtilities.CreateInstance<ActiveBasketHealthCheck>(sp),
    failureStatus: null,
    tags: new[] { ObservabilityRegistration.ReadyTag }));

healthChecks.Add(new HealthCheckRegistration(
    name: "corp-actions",
    factory: sp => ActivatorUtilities.CreateInstance<CorporateActionHealthCheck>(sp),
    failureStatus: null,
    tags: new[] { ObservabilityRegistration.ReadyTag }));

var app = builder.Build();

app.Services.LogConfigurationPosture(
    "hqqq-reference-data",
    app.Logger,
    "Kafka", "Redis", "Timescale", "ReferenceData");

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
