using Hqqq.Infrastructure.Hosting;
using Hqqq.Observability.Health;
using Hqqq.Observability.Logging;
using Hqqq.Gateway.Configuration;
using Hqqq.Gateway.Endpoints;
using Hqqq.Gateway.Hubs;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddLegacyFlatKeyFallback();

builder.Services.AddHqqqRedis(builder.Configuration);
builder.Services.AddHqqqRedisConnection();
builder.Services.AddHqqqTimescale(builder.Configuration);
builder.Services.AddHqqqObservability();

builder.Services.AddGatewaySources(builder.Configuration, builder.Environment);

builder.Services.AddSignalR();

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("Process is running"), tags: ["live"]);

var app = builder.Build();

app.Services.LogConfigurationPosture(
    "hqqq-gateway",
    app.Logger,
    "Redis", "Timescale", "Gateway");

app.MapHealthChecks("/healthz/live", new()
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(HealthPayloadBuilder.Build(report));
    },
});

app.MapHealthChecks("/healthz/ready", new()
{
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(HealthPayloadBuilder.Build(report));
    },
});

app.MapGatewayEndpoints();
app.MapHub<MarketHub>("/hubs/market");

// Phase 2B5 — IQuoteSource and IConstituentsSource now support Redis-backed
// implementations selectable via Gateway:Sources:Quote / Gateway:Sources:Constituents.
// TODO: Phase 2C3 — swap IHistorySource to TimescaleHistorySource (Timescale-backed)
// TODO: Phase 2C3 — swap ISystemHealthSource to gateway-native health aggregation
// TODO: Phase 2D2 — wire Redis pub/sub backplane for SignalR fan-out on /hubs/market

app.Run();

public partial class Program { }
