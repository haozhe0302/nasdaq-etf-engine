using Hqqq.Infrastructure.Hosting;
using Hqqq.Observability.Health;
using Hqqq.Observability.Logging;
using Hqqq.Gateway.Hubs;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddLegacyFlatKeyFallback();

builder.Services.AddHqqqRedis(builder.Configuration);
builder.Services.AddHqqqTimescale(builder.Configuration);
builder.Services.AddHqqqObservability();

builder.Services.AddSignalR();

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("Process is running"), tags: ["live"]);

var app = builder.Build();

app.Services.LogConfigurationPosture(
    "hqqq-gateway",
    app.Logger,
    "Redis", "Timescale");

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

app.MapGet("/api/quote", () =>
    Results.Json(new { status = "not_wired", message = "Gateway not yet connected to Redis (Phase 2B)" },
        statusCode: 503));

app.MapHub<MarketHub>("/hubs/market");

// TODO: Phase 2B — add Redis-backed GET /api/quote with real iNAV data
// TODO: Phase 2B — add GET /api/basket/constituents from Timescale
// TODO: Phase 2B — add GET /api/history/* from Timescale
// TODO: Phase 2B — wire Redis pub/sub backplane for SignalR

app.Run();

public partial class Program { }
