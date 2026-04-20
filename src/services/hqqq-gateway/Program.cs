using Hqqq.Infrastructure.Hosting;
using Hqqq.Observability.Hosting;
using Hqqq.Gateway.Configuration;
using Hqqq.Gateway.Endpoints;
using Hqqq.Gateway.Hubs;
using Hqqq.Gateway.Services.Realtime;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddLegacyFlatKeyFallback();

builder.Services.AddHqqqRedis(builder.Configuration);
builder.Services.AddHqqqRedisConnection();
builder.Services.AddHqqqTimescale(builder.Configuration);
builder.Services.AddHqqqOperatingMode(builder.Configuration);

// Phase 2D1 — shared observability: ServiceIdentity, HqqqMetrics,
// HealthChecks (self/live), Prometheus MeterProvider exporter.
builder.Services.AddHqqqObservability("hqqq-gateway", builder.Environment)
    .AddRedisHealthCheck()
    .AddTimescaleHealthCheck();
builder.Services.AddHqqqMetricsExporter();

builder.Services.AddGatewaySources(builder.Configuration, builder.Environment);

builder.Services.AddSignalR();

// Phase 2D2 — bridge Redis pub/sub to SignalR. Each gateway instance
// subscribes to RedisKeys.QuoteUpdateChannel and broadcasts QuoteUpdate
// events to its own connected clients. No SignalR Redis backplane needed:
// the engine publishes once, every gateway receives the message, each one
// fans out locally.
builder.Services.AddSingleton<QuoteUpdateBroadcaster>();
builder.Services.AddHostedService<QuoteUpdateSubscriber>();

var app = builder.Build();

app.Services.LogConfigurationPosture(
    "hqqq-gateway",
    app.Logger,
    "Redis", "Timescale", "Gateway");

app.MapHqqqHealthEndpoints();

app.MapGatewayEndpoints();
app.MapHub<MarketHub>("/hubs/market");

// Phase 2B5 — IQuoteSource and IConstituentsSource support Redis-backed
// implementations selectable via Gateway:Sources:Quote / Gateway:Sources:Constituents.
// Phase 2C2 — IHistorySource supports a Timescale-backed implementation
// selectable via Gateway:Sources:History=timescale, reading quote_snapshots
// directly. Stub/legacy remain available as fallbacks.
// Phase 2D1 — /api/system/health is served natively by AggregatedSystemHealthSource
// which fans out to each service's /healthz/ready and the local infra probes.
// Phase 2D2 — QuoteUpdateSubscriber bridges hqqq:channel:quote-update to
// MarketHub broadcasts. Reconnect/bootstrap remains REST GET /api/quote.

app.Run();

public partial class Program { }
