using Hqqq.Infrastructure.Hosting;
using Hqqq.Observability.Hosting;
using Hqqq.ReferenceData.Endpoints;
using Hqqq.ReferenceData.Jobs;
using Hqqq.ReferenceData.Repositories;
using Hqqq.ReferenceData.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddLegacyFlatKeyFallback();

builder.Services.AddHqqqKafka(builder.Configuration);
builder.Services.AddHqqqRedis(builder.Configuration);
builder.Services.AddHqqqTimescale(builder.Configuration);

// Phase 2D1 — shared observability + dependency probes for the deps this
// service actually uses (Kafka for basket events, Redis/Timescale for the
// upcoming basket-state writers).
builder.Services.AddHqqqObservability("hqqq-reference-data", builder.Environment)
    .AddKafkaHealthCheck();
builder.Services.AddHqqqMetricsExporter();

builder.Services.AddSingleton<IBasketRepository, InMemoryBasketRepository>();
builder.Services.AddSingleton<IBasketService, StubBasketService>();
builder.Services.AddHostedService<BasketRefreshJob>();

var app = builder.Build();

app.Services.LogConfigurationPosture(
    "hqqq-reference-data",
    app.Logger,
    "Kafka", "Redis", "Timescale");

app.MapHqqqHealthEndpoints();

app.MapBasketEndpoints();

// TODO: Phase 2B — register Kafka producer for refdata.basket.active.v1 / refdata.basket.events.v1
// TODO: Phase 2B — replace InMemoryBasketRepository with Timescale-backed implementation
// TODO: Phase 2B — wire real basket refresh from data sources

app.Run();

public partial class Program { }
