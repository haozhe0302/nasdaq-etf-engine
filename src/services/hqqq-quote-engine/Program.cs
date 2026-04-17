using Hqqq.Infrastructure.Hosting;
using Hqqq.Observability.Logging;
using Hqqq.QuoteEngine.Abstractions;
using Hqqq.QuoteEngine.Feeds;
using Hqqq.QuoteEngine.Services;
using Hqqq.QuoteEngine.State;
using Hqqq.QuoteEngine.Workers;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddLegacyFlatKeyFallback();
builder.Logging.AddHqqqDefaults();

builder.Services.AddHqqqKafka(builder.Configuration);
builder.Services.AddHqqqRedis(builder.Configuration);
builder.Services.AddHqqqObservability();

// ── Engine core ──────────────────────────────────────────────
builder.Services.AddSingleton<ISystemClock, SystemClock>();
builder.Services.AddSingleton<QuoteEngineOptions>();
builder.Services.AddSingleton<PerSymbolQuoteStore>();
builder.Services.AddSingleton<BasketStateStore>();
builder.Services.AddSingleton(sp => new EngineRuntimeState(
    sp.GetRequiredService<QuoteEngineOptions>().SeriesCapacity));
builder.Services.AddSingleton<IncrementalNavCalculator>();
builder.Services.AddSingleton<SnapshotMaterializer>();
builder.Services.AddSingleton<QuoteDeltaMaterializer>();
builder.Services.AddSingleton<IQuoteEngine, QuoteEngine>();

// ── Feeds (B2: in-memory fakes; B3 swaps to Kafka-backed implementations) ──
builder.Services.AddSingleton<InMemoryRawTickFeed>();
builder.Services.AddSingleton<IRawTickFeed>(sp => sp.GetRequiredService<InMemoryRawTickFeed>());
builder.Services.AddSingleton<IRawTickSink>(sp => sp.GetRequiredService<InMemoryRawTickFeed>());

builder.Services.AddSingleton<InMemoryBasketStateFeed>();
builder.Services.AddSingleton<IBasketStateFeed>(sp => sp.GetRequiredService<InMemoryBasketStateFeed>());
builder.Services.AddSingleton<IBasketStateSink>(sp => sp.GetRequiredService<InMemoryBasketStateFeed>());

builder.Services.AddHostedService<QuoteEngineWorker>();

var host = builder.Build();

host.Services.LogConfigurationPosture(
    "hqqq-quote-engine",
    host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup"),
    "Kafka", "Redis");

// TODO: B3 — replace InMemoryRawTickFeed with a Kafka consumer on market.raw_ticks.v1
// TODO: B3 — replace InMemoryBasketStateFeed with a Kafka consumer on refdata.basket.active.v1
// TODO: B3 — publish materialized snapshots to pricing.snapshots.v1 + write Redis cache
// TODO: B4 — hqqq-gateway consumes snapshots and fans out over REST + SignalR

host.Run();
