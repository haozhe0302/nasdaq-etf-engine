using Hqqq.Infrastructure.Hosting;
using Hqqq.Observability.Hosting;
using Hqqq.Observability.Logging;
using Hqqq.Ingress.Clients;
using Hqqq.Ingress.Configuration;
using Hqqq.Ingress.Health;
using Hqqq.Ingress.Publishing;
using Hqqq.Ingress.State;
using Hqqq.Ingress.Workers;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddLegacyFlatKeyFallback();
builder.Logging.AddHqqqDefaults();

builder.Services.Configure<TiingoOptions>(
    builder.Configuration.GetSection("Tiingo"));
builder.Services.AddHqqqKafka(builder.Configuration);
builder.Services.AddHqqqOperatingMode(builder.Configuration);

// Phase 2D1 — shared observability + Kafka dependency probe. The
// management host exposes /healthz/* and /metrics on Management:Port.
builder.Services.AddHqqqObservability("hqqq-ingress", builder.Environment)
    .AddKafkaHealthCheck()
    .Add(new HealthCheckRegistration(
        "tiingo-upstream",
        sp => ActivatorUtilities.CreateInstance<IngressUpstreamHealthCheck>(sp),
        failureStatus: null,
        tags: new[] { ObservabilityRegistration.ReadyTag }));
builder.Services.AddHqqqManagementHost(builder.Configuration);

builder.Services.AddSingleton<IngestionState>();

// Branch on operating mode: hybrid keeps the historic stub posture
// (legacy monolith bridges ticks); standalone activates real Tiingo
// websocket + REST snapshot + Kafka publishing.
var mode = OperatingModeRegistration.ResolveMode(builder.Configuration);
if (mode == OperatingMode.Standalone)
{
    builder.Services.AddHttpClient();
    builder.Services.AddSingleton<ITiingoStreamClient, TiingoStreamClient>();
    builder.Services.AddSingleton<ITiingoSnapshotClient, TiingoSnapshotClient>();
    builder.Services.AddSingleton<ITickPublisher, KafkaTickPublisher>();
}
else
{
    builder.Services.AddSingleton<ITiingoStreamClient, StubTiingoStreamClient>();
    builder.Services.AddSingleton<ITiingoSnapshotClient, StubTiingoSnapshotClient>();
    builder.Services.AddSingleton<ITickPublisher, LoggingTickPublisher>();
}

builder.Services.AddHostedService<TiingoIngressWorker>();

var host = builder.Build();

host.Services.LogConfigurationPosture(
    "hqqq-ingress",
    host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup"),
    "Tiingo", "Kafka", "Management");

host.Run();

public partial class Program { }
