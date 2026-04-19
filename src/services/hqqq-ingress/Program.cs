using Hqqq.Infrastructure.Hosting;
using Hqqq.Observability.Hosting;
using Hqqq.Observability.Logging;
using Hqqq.Ingress.Clients;
using Hqqq.Ingress.Configuration;
using Hqqq.Ingress.Publishing;
using Hqqq.Ingress.State;
using Hqqq.Ingress.Workers;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddLegacyFlatKeyFallback();
builder.Logging.AddHqqqDefaults();

builder.Services.Configure<TiingoOptions>(
    builder.Configuration.GetSection("Tiingo"));
builder.Services.AddHqqqKafka(builder.Configuration);

// Phase 2D1 — shared observability + Kafka dependency probe. The
// management host exposes /healthz/* and /metrics on Management:Port.
builder.Services.AddHqqqObservability("hqqq-ingress", builder.Environment)
    .AddKafkaHealthCheck();
builder.Services.AddHqqqManagementHost(builder.Configuration);

builder.Services.AddSingleton<ITiingoStreamClient, StubTiingoStreamClient>();
builder.Services.AddSingleton<ITiingoSnapshotClient, StubTiingoSnapshotClient>();
builder.Services.AddSingleton<ITickPublisher, LoggingTickPublisher>();
builder.Services.AddSingleton<IngestionState>();
builder.Services.AddHostedService<TiingoIngressWorker>();

var host = builder.Build();

host.Services.LogConfigurationPosture(
    "hqqq-ingress",
    host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup"),
    "Tiingo", "Kafka", "Management");

// TODO: Phase 2B — replace StubTiingoStreamClient/StubTiingoSnapshotClient with real implementations
// TODO: Phase 2B — replace LoggingTickPublisher with KafkaTickPublisher

host.Run();

public partial class Program { }
