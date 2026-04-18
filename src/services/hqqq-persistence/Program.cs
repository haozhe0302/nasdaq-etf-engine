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

// Shared NpgsqlDataSource so schema bootstrap and the writer pool share one connection pool.
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
builder.Services.AddSingleton<QuoteSnapshotSchemaBootstrapper>();

// Hosted-service registration order matters: SchemaBootstrapHostedService is
// a plain IHostedService whose StartAsync completes before the consumer and
// worker start — so by the time snapshots arrive, the destination table
// already exists. The consumer pushes into the sink; the worker drains.
builder.Services.AddHostedService<SchemaBootstrapHostedService>();
builder.Services.AddHostedService<QuoteSnapshotConsumer>();
builder.Services.AddHostedService<QuoteSnapshotPersistenceWorker>();

var host = builder.Build();

host.Services.LogConfigurationPosture(
    "hqqq-persistence",
    host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup"),
    "Kafka", "Timescale", "Persistence");

host.Run();
