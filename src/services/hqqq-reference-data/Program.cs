using Hqqq.Infrastructure.Hosting;
using Hqqq.Observability.Health;
using Hqqq.Observability.Logging;
using Hqqq.ReferenceData.Endpoints;
using Hqqq.ReferenceData.Jobs;
using Hqqq.ReferenceData.Repositories;
using Hqqq.ReferenceData.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddLegacyFlatKeyFallback();

builder.Services.AddHqqqKafka(builder.Configuration);
builder.Services.AddHqqqRedis(builder.Configuration);
builder.Services.AddHqqqTimescale(builder.Configuration);
builder.Services.AddHqqqObservability();

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("Process is running"), tags: ["live"]);

builder.Services.AddSingleton<IBasketRepository, InMemoryBasketRepository>();
builder.Services.AddSingleton<IBasketService, StubBasketService>();
builder.Services.AddHostedService<BasketRefreshJob>();

var app = builder.Build();

app.Services.LogConfigurationPosture(
    "hqqq-reference-data",
    app.Logger,
    "Kafka", "Redis", "Timescale");

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

app.MapBasketEndpoints();

// TODO: Phase 2B — register Kafka producer for refdata.basket.active.v1 / refdata.basket.events.v1
// TODO: Phase 2B — replace InMemoryBasketRepository with Timescale-backed implementation
// TODO: Phase 2B — wire real basket refresh from data sources

app.Run();

public partial class Program { }
