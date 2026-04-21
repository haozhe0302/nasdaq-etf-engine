using Hqqq.Infrastructure.Hosting;
using Hqqq.Observability.Hosting;
using Hqqq.Observability.Logging;
using Hqqq.Ingress.Clients;
using Hqqq.Ingress.Configuration;
using Hqqq.Ingress.Consumers;
using Hqqq.Ingress.Health;
using Hqqq.Ingress.Metrics;
using Hqqq.Ingress.Publishing;
using Hqqq.Ingress.State;
using Hqqq.Ingress.Workers;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddLegacyFlatKeyFallback();
builder.Logging.AddHqqqDefaults();

builder.Services.Configure<TiingoOptions>(
    builder.Configuration.GetSection("Tiingo"));
builder.Services.Configure<IngressBasketOptions>(
    builder.Configuration.GetSection(IngressBasketOptions.SectionName));
builder.Services.AddHqqqKafka(builder.Configuration);

// OperatingMode is kept as a logging-posture tag (cross-cutting consistency
// across Phase 2 services); it no longer branches runtime behaviour in
// ingress. Phase 2 ingress always opens the real Tiingo websocket and
// publishes to Kafka — there is no "hybrid" stub path.
builder.Services.AddHqqqOperatingMode(builder.Configuration);

builder.Services.AddHqqqObservability("hqqq-ingress", builder.Environment)
    .AddKafkaHealthCheck()
    .Add(new HealthCheckRegistration(
        "tiingo-upstream",
        sp => ActivatorUtilities.CreateInstance<IngressUpstreamHealthCheck>(sp),
        failureStatus: null,
        tags: new[] { ObservabilityRegistration.ReadyTag }))
    .Add(new HealthCheckRegistration(
        "ingress-basket",
        sp => ActivatorUtilities.CreateInstance<IngressBasketHealthCheck>(sp),
        failureStatus: null,
        tags: new[] { ObservabilityRegistration.ReadyTag }));
builder.Services.AddHqqqManagementHost(builder.Configuration);

builder.Services.AddSingleton<IngestionState>();
builder.Services.AddSingleton<ActiveSymbolUniverse>();

// Single, real runtime path — no mode branching.
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ITiingoStreamClient, TiingoStreamClient>();
builder.Services.AddSingleton<ITiingoSnapshotClient, TiingoSnapshotClient>();
builder.Services.AddSingleton<ITickPublisher, KafkaTickPublisher>();

// Active-symbol universe is sourced from refdata.basket.active.v1. The
// coordinator diffs fingerprints across basket events and drives the
// websocket subscribe/unsubscribe. The Kafka consumer feeds the
// coordinator via ActiveSymbolUniverse.
builder.Services.AddSingleton<BasketSubscriptionCoordinator>();
builder.Services.AddHostedService<BasketActiveConsumer>();
builder.Services.AddHostedService<TiingoIngressWorker>();

// Observable gauges for the basket-driven subscription and the runtime
// tick-flow signal. Eagerly resolved below so the gauges are wired
// before the first /metrics scrape.
builder.Services.AddSingleton<IngressMetrics>();

var host = builder.Build();

_ = host.Services.GetRequiredService<IngressMetrics>();

host.Services.LogConfigurationPosture(
    "hqqq-ingress",
    host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup"),
    "Tiingo", "Kafka", "Management", "Ingress");

host.Run();

public partial class Program { }
