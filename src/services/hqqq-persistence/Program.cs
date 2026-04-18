using Hqqq.Infrastructure.Hosting;
using Hqqq.Infrastructure.Timescale;
using Hqqq.Observability.Logging;
using Hqqq.Persistence.Abstractions;
using Hqqq.Persistence.Consumers;
using Hqqq.Persistence.Feeds;
using Hqqq.Persistence.Options;
using Hqqq.Persistence.Persistence;
using Hqqq.Persistence.Schema;
using Hqqq.Persistence.Workers;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddLegacyFlatKeyFallback();
builder.Logging.AddHqqqDefaults();

builder.Services.AddHqqqKafka(builder.Configuration);
builder.Services.AddHqqqTimescale(builder.Configuration);
builder.Services.AddHqqqObservability();

builder.Services.Configure<PersistenceOptions>(builder.Configuration.GetSection("Persistence"));

// Shared NpgsqlDataSource so every schema bootstrapper and every writer
// share one connection pool.
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<TimescaleOptions>>().Value;
    var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(TimescaleConnectionFactory));
    return TimescaleConnectionFactory.Create(options, logger);
});

// ── Quote snapshot pipeline (C1): Kafka → channel → batch writer → Timescale ──
builder.Services.AddSingleton<InMemoryQuoteSnapshotFeed>();
builder.Services.AddSingleton<IQuoteSnapshotFeed>(sp => sp.GetRequiredService<InMemoryQuoteSnapshotFeed>());
builder.Services.AddSingleton<IQuoteSnapshotSink>(sp => sp.GetRequiredService<InMemoryQuoteSnapshotFeed>());
builder.Services.AddSingleton<IQuoteSnapshotWriter, TimescaleQuoteSnapshotWriter>();

// ── Raw tick pipeline (C3): Kafka → channel → batch writer → Timescale ──
// Independent singleton instance so a failure in one pipeline's channel
// or writer cannot block the other.
builder.Services.AddSingleton<InMemoryRawTickFeed>();
builder.Services.AddSingleton<IRawTickFeed>(sp => sp.GetRequiredService<InMemoryRawTickFeed>());
builder.Services.AddSingleton<IRawTickSink>(sp => sp.GetRequiredService<InMemoryRawTickFeed>());
builder.Services.AddSingleton<IRawTickWriter, TimescaleRawTickWriter>();

// ── Schema / rollup / retention bootstrappers ──
builder.Services.AddSingleton<QuoteSnapshotSchemaBootstrapper>();
builder.Services.AddSingleton<RawTickSchemaBootstrapper>();
builder.Services.AddSingleton<QuoteSnapshotRollupBootstrapper>();
builder.Services.AddSingleton<RetentionPolicyBootstrapper>();

// Hosted-service registration order matters: SchemaBootstrapHostedService is
// a plain IHostedService whose StartAsync completes before any consumer or
// worker starts — so by the time events arrive, every destination table,
// rollup, and retention policy is in place.
builder.Services.AddHostedService<SchemaBootstrapHostedService>();
builder.Services.AddHostedService<QuoteSnapshotConsumer>();
builder.Services.AddHostedService<QuoteSnapshotPersistenceWorker>();
builder.Services.AddHostedService<RawTickConsumer>();
builder.Services.AddHostedService<RawTickPersistenceWorker>();

var host = builder.Build();

host.Services.LogConfigurationPosture(
    "hqqq-persistence",
    host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup"),
    "Kafka", "Timescale", "Persistence");

host.Run();
