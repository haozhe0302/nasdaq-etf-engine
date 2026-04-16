using Hqqq.Infrastructure.Hosting;
using Hqqq.Observability.Logging;
using Hqqq.Analytics;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddLegacyFlatKeyFallback();
builder.Logging.AddHqqqDefaults();

builder.Services.AddHqqqKafka(builder.Configuration);
builder.Services.AddHqqqTimescale(builder.Configuration);
builder.Services.AddHqqqObservability();

builder.Services.AddHostedService<AnalyticsWorker>();

var host = builder.Build();

host.Services.LogConfigurationPosture(
    "hqqq-analytics",
    host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup"),
    "Kafka", "Timescale");

// TODO: Phase 2B — add Kafka consumer for ops.incidents.v1
// TODO: Phase 2C — add replay/backfill job runner
// TODO: Phase 2C — add anomaly detection pipeline

host.Run();
