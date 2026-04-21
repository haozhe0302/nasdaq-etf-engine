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

// Phase 2 default per-endpoint posture is `redis` / `redis` /
// `timescale` / `aggregated` and never requires the legacy hqqq-api
// monolith. The `legacy` source mode remains in the codebase only for
// side-by-side parity testing against a separately-running monolith;
// log a loud warning on startup so this opt-in fallback can never
// silently become production behaviour.
{
    var resolvedModes = app.Services.GetRequiredService<ResolvedSourceModes>();

    // Phase-2-native resolved-mode startup summary. Printed once per
    // container start so the exact source wiring is visible in
    // centralised logs without having to scrape DI diagnostics.
    app.Logger.LogInformation(
        "[gateway:resolved-modes] Quote={Quote} Constituents={Constituents} History={History} SystemHealth={SystemHealth}",
        resolvedModes.Quote, resolvedModes.Constituents,
        resolvedModes.History, resolvedModes.SystemHealth);

    var legacyEndpoints = new List<string>();
    if (resolvedModes.Quote == GatewayDataSourceMode.Legacy) legacyEndpoints.Add("Quote");
    if (resolvedModes.Constituents == GatewayDataSourceMode.Legacy) legacyEndpoints.Add("Constituents");
    if (resolvedModes.History == GatewayDataSourceMode.Legacy) legacyEndpoints.Add("History");
    if (resolvedModes.SystemHealth == GatewayDataSourceMode.Legacy) legacyEndpoints.Add("SystemHealth");

    if (legacyEndpoints.Count > 0)
    {
        app.Logger.LogWarning(
            "[gateway:legacy-mode] Gateway is forwarding {Endpoints} to legacy hqqq-api via Gateway:LegacyBaseUrl. " +
            "This is a non-default Phase 2 fallback intended for parity testing only — switch to the Phase-2-native " +
            "sources (Redis / Timescale / aggregated) for normal operation.",
            string.Join(", ", legacyEndpoints));
    }
}

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
