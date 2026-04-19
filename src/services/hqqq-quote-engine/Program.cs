using Hqqq.Infrastructure.Hosting;
using Hqqq.Observability.Hosting;
using Hqqq.Observability.Logging;
using Hqqq.QuoteEngine.Abstractions;
using Hqqq.QuoteEngine.Consumers;
using Hqqq.QuoteEngine.Feeds;
using Hqqq.QuoteEngine.Persistence;
using Hqqq.QuoteEngine.Publishing;
using Hqqq.QuoteEngine.Services;
using Hqqq.QuoteEngine.State;
using Hqqq.QuoteEngine.Workers;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddLegacyFlatKeyFallback();
builder.Logging.AddHqqqDefaults();

builder.Services.AddHqqqKafka(builder.Configuration);
builder.Services.AddHqqqRedis(builder.Configuration);
builder.Services.AddHqqqRedisConnection();

// Phase 2D1 — shared observability + Kafka/Redis dependency probes;
// management host serves /healthz/* and /metrics on Management:Port.
builder.Services.AddHqqqObservability("hqqq-quote-engine", builder.Environment)
    .AddKafkaHealthCheck()
    .AddRedisHealthCheck();
builder.Services.AddHqqqManagementHost(builder.Configuration);

builder.Services.Configure<QuoteEngineOptions>(builder.Configuration.GetSection("QuoteEngine"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<QuoteEngineOptions>>().Value);

// ── Engine core ──────────────────────────────────────────────
builder.Services.AddSingleton<ISystemClock, SystemClock>();
builder.Services.AddSingleton<PerSymbolQuoteStore>();
builder.Services.AddSingleton<BasketStateStore>();
builder.Services.AddSingleton(sp => new EngineRuntimeState(
    sp.GetRequiredService<QuoteEngineOptions>().SeriesCapacity));
builder.Services.AddSingleton<IncrementalNavCalculator>();
builder.Services.AddSingleton<SnapshotMaterializer>();
builder.Services.AddSingleton<QuoteDeltaMaterializer>();
builder.Services.AddSingleton<ConstituentsSnapshotMaterializer>();
builder.Services.AddSingleton<QuoteSnapshotV1Mapper>();
builder.Services.AddSingleton<IQuoteEngine, QuoteEngine>();

// ── Output sinks: Redis latest state + Kafka snapshot events ─
builder.Services.AddSingleton<IQuoteSnapshotSink, RedisSnapshotWriter>();
builder.Services.AddSingleton<IConstituentSnapshotSink, RedisConstituentsWriter>();
builder.Services.AddSingleton<ConfluentPricingSnapshotProducer>();
builder.Services.AddSingleton<IPricingSnapshotProducer>(sp =>
    sp.GetRequiredService<ConfluentPricingSnapshotProducer>());
builder.Services.AddSingleton<ISnapshotEventPublisher, SnapshotTopicPublisher>();

// ── In-process buffers between Kafka consumers and the pipeline worker ──
// The consumers push into the sink side; the pipeline worker drains the
// feed side. Keeping the channel in the middle preserves backpressure and
// lets tests exercise the engine without a live broker.
builder.Services.AddSingleton<InMemoryRawTickFeed>();
builder.Services.AddSingleton<IRawTickFeed>(sp => sp.GetRequiredService<InMemoryRawTickFeed>());
builder.Services.AddSingleton<IRawTickSink>(sp => sp.GetRequiredService<InMemoryRawTickFeed>());

builder.Services.AddSingleton<InMemoryBasketStateFeed>();
builder.Services.AddSingleton<IBasketStateFeed>(sp => sp.GetRequiredService<InMemoryBasketStateFeed>());
builder.Services.AddSingleton<IBasketStateSink>(sp => sp.GetRequiredService<InMemoryBasketStateFeed>());

// ── Checkpoint persistence ───────────────────────────────────
builder.Services.AddSingleton<IEngineCheckpointStore, FileEngineCheckpointStore>();

// ── Kafka consumers ──────────────────────────────────────────
// Singletons so the restorer can prime BasketEventConsumer's fingerprint guard.
builder.Services.AddSingleton<BasketEventConsumer>();
builder.Services.AddSingleton<RawTickConsumer>();

// Hosted-service registration order matters: the restorer is a plain
// IHostedService whose StartAsync completes synchronously (restore → prime),
// so by the time the consumer BackgroundServices start, the engine already
// sees any persisted basket and the consumer guard is primed.
builder.Services.AddHostedService<EngineCheckpointRestorer>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BasketEventConsumer>());
builder.Services.AddHostedService(sp => sp.GetRequiredService<RawTickConsumer>());
builder.Services.AddHostedService<QuoteEngineWorker>();

var host = builder.Build();

host.Services.LogConfigurationPosture(
    "hqqq-quote-engine",
    host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup"),
    "Kafka", "Redis", "QuoteEngine", "Management");

host.Run();

public partial class Program { }
