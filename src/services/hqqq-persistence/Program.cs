using Hqqq.Infrastructure.Hosting;
using Hqqq.Observability.Logging;
using Hqqq.Persistence;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddLegacyFlatKeyFallback();
builder.Logging.AddHqqqDefaults();

builder.Services.AddHqqqKafka(builder.Configuration);
builder.Services.AddHqqqTimescale(builder.Configuration);
builder.Services.AddHqqqObservability();

builder.Services.AddHostedService<PersistenceWorker>();

var host = builder.Build();

host.Services.LogConfigurationPosture(
    "hqqq-persistence",
    host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup"),
    "Kafka", "Timescale");

// TODO: Phase 2B — add Kafka consumer for market.raw_ticks.v1 and pricing.snapshots.v1
// TODO: Phase 2B — add Timescale batch writer for tick/snapshot persistence
// TODO: Phase 2B — add retention/aggregation job scheduling

host.Run();
