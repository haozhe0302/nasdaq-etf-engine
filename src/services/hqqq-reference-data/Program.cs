using Hqqq.Infrastructure.Hosting;
using Hqqq.Observability.Hosting;
using Hqqq.ReferenceData.Basket;
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
using Microsoft.Extensions.Options;

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

// Phase 2 basket pipeline (ported from the Phase 1 Basket module). All
// four Phase 1 adapters are registered; the scrapers (StockAnalysis,
// Schwab) default OFF in appsettings and are flipped ON by Azure
// Production bicep env injection so the standard Production path is
// the full four-source anchored pipeline with real SharesHeld.
builder.Services.AddHttpClient(StockAnalysisBasketAdapter.HttpClientName);
builder.Services.AddHttpClient(SchwabBasketAdapter.HttpClientName);
builder.Services.AddHttpClient(AlphaVantageBasketAdapter.HttpClientName);
builder.Services.AddHttpClient(NasdaqBasketAdapter.HttpClientName);
builder.Services.AddSingleton<StockAnalysisBasketAdapter>();
builder.Services.AddSingleton<SchwabBasketAdapter>();
builder.Services.AddSingleton<AlphaVantageBasketAdapter>();
builder.Services.AddSingleton<NasdaqBasketAdapter>();
builder.Services.AddSingleton<RawSourceCache>();
builder.Services.AddSingleton<MergedBasketCache>();
builder.Services.AddSingleton<PendingBasketStore>();
builder.Services.AddSingleton<RealSourceBasketPipeline>();
builder.Services.AddSingleton<RealSourceBasketHoldingsSource>();

builder.Services.AddSingleton<IHoldingsSource>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<ReferenceDataOptions>>();
    var validator = sp.GetRequiredService<HoldingsValidator>();
    var fallback = sp.GetRequiredService<FallbackSeedHoldingsSource>();
    var environment = sp.GetRequiredService<IWebHostEnvironment>();
    var logger = sp.GetRequiredService<ILogger<CompositeHoldingsSource>>();
    var primaries = new List<IHoldingsSource>();
    if (opts.Value.Basket.Mode == BasketMode.RealSource)
    {
        primaries.Add(sp.GetRequiredService<RealSourceBasketHoldingsSource>());
    }
    primaries.Add(sp.GetRequiredService<LiveHoldingsSource>());
    return new CompositeHoldingsSource(primaries, fallback, validator, opts, environment, logger);
});

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
builder.Services.AddSingleton<BasketRefreshPipeline>(sp => new BasketRefreshPipeline(
    sp.GetRequiredService<IHoldingsSource>(),
    sp.GetRequiredService<HoldingsValidator>(),
    sp.GetRequiredService<CorporateActionAdjustmentService>(),
    sp.GetRequiredService<BasketTransitionPlanner>(),
    sp.GetRequiredService<ActiveBasketStore>(),
    sp.GetRequiredService<IBasketPublisher>(),
    sp.GetRequiredService<PublishHealthTracker>(),
    sp.GetRequiredService<ILogger<BasketRefreshPipeline>>(),
    sp.GetService<TimeProvider>(),
    sp.GetService<Hqqq.Observability.Metrics.HqqqMetrics>(),
    sp.GetService<PendingBasketStore>()));
builder.Services.AddSingleton<IBasketService, BasketService>();
builder.Services.AddSingleton<IBasketPublisher, KafkaBasketPublisher>();
builder.Services.AddHostedService<BasketRefreshJob>();
builder.Services.AddHostedService<BasketLifecycleScheduler>();

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

// Production fail-fast: deterministic seed fallback, anchor-less proxy
// posture, and offline-only corp-actions are only permitted with
// explicit operator opt-in.
var refOptions = app.Services.GetRequiredService<IOptions<ReferenceDataOptions>>().Value;
ReferenceDataStartupGuard.Validate(app.Environment, refOptions, app.Logger);
ReferenceDataStartupGuard.ValidateCorporateActions(app.Environment, refOptions, app.Logger);

// Loud, structured posture line so the deploy log carries the truth.
app.Logger.LogInformation(
    "ReferenceData posture: env={Env} basket.mode={Mode} anchors=[stockanalysis={SA},schwab={SC}] tail=[alphavantage={AV},nasdaq={ND}] requireAnchor={RequireAnchor} allowAnchorlessProxy={AllowProxy} allowDeterministicSeedInProd={AllowSeed} tiingoCorpActions={TiingoCA} allowOfflineOnlyCorpActions={OfflineCA}",
    app.Environment.EnvironmentName,
    refOptions.Basket.Mode,
    refOptions.Basket.Sources.StockAnalysis.Enabled,
    refOptions.Basket.Sources.Schwab.Enabled,
    refOptions.Basket.Sources.AlphaVantage.Enabled,
    refOptions.Basket.Sources.Nasdaq.Enabled,
    refOptions.Basket.RequireAnchorInProduction,
    refOptions.Basket.AllowAnchorlessProxyInProduction,
    refOptions.Basket.AllowDeterministicSeedInProduction,
    refOptions.CorporateActions.Tiingo.Enabled,
    refOptions.CorporateActions.AllowOfflineOnlyInProduction);

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
