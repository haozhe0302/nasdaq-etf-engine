using Hqqq.Infrastructure.Hosting;
using Hqqq.Observability.Logging;
using Hqqq.QuoteEngine;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddLegacyFlatKeyFallback();
builder.Logging.AddHqqqDefaults();

builder.Services.AddHqqqKafka(builder.Configuration);
builder.Services.AddHqqqRedis(builder.Configuration);
builder.Services.AddHqqqObservability();

builder.Services.AddHostedService<QuoteEngineWorker>();

var host = builder.Build();

host.Services.LogConfigurationPosture(
    "hqqq-quote-engine",
    host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup"),
    "Kafka", "Redis");

// TODO: Phase 2B — add Kafka consumer for market.raw_ticks.v1 and refdata.basket.active.v1
// TODO: Phase 2B — add Redis write for computed iNAV snapshots
// TODO: Phase 2B — add Kafka producer for pricing.snapshots.v1

host.Run();
