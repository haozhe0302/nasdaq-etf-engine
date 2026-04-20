using Hqqq.Infrastructure.Hosting;
using Hqqq.Observability.Hosting;
using Hqqq.ReferenceData.Endpoints;
using Hqqq.ReferenceData.Health;
using Hqqq.ReferenceData.Jobs;
using Hqqq.ReferenceData.Publishing;
using Hqqq.ReferenceData.Repositories;
using Hqqq.ReferenceData.Services;
using Hqqq.ReferenceData.Standalone;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddLegacyFlatKeyFallback();

builder.Services.AddHqqqKafka(builder.Configuration);
builder.Services.AddHqqqRedis(builder.Configuration);
builder.Services.AddHqqqTimescale(builder.Configuration);
builder.Services.AddHqqqOperatingMode(builder.Configuration);

builder.Services.Configure<BasketSeedOptions>(
    builder.Configuration.GetSection(BasketSeedOptions.SectionName));

var healthChecks = builder.Services.AddHqqqObservability("hqqq-reference-data", builder.Environment)
    .AddKafkaHealthCheck();
builder.Services.AddHqqqMetricsExporter();

var mode = OperatingModeRegistration.ResolveMode(builder.Configuration);

if (mode == OperatingMode.Standalone)
{
    // Standalone: load deterministic seed up-front so a malformed seed
    // surfaces as a startup crash (the Container Apps revision will fail
    // to become Ready, which is what we want).
    builder.Services.AddSingleton<BasketSeedLoader>();
    builder.Services.AddSingleton(sp => sp.GetRequiredService<BasketSeedLoader>().Load());
    builder.Services.AddSingleton<SeedFileBasketRepository>();
    builder.Services.AddSingleton<IBasketRepository>(sp => sp.GetRequiredService<SeedFileBasketRepository>());
    builder.Services.AddSingleton<IBasketPublisher, KafkaBasketPublisher>();
    builder.Services.AddHostedService<StandalonePublishJob>();

    healthChecks.Add(new HealthCheckRegistration(
        name: "basket-seed",
        factory: sp => ActivatorUtilities.CreateInstance<BasketSeedHealthCheck>(sp),
        failureStatus: null,
        tags: new[] { ObservabilityRegistration.ReadyTag }));
}
else
{
    builder.Services.AddSingleton<IBasketRepository, InMemoryBasketRepository>();
}

builder.Services.AddSingleton<IBasketService, StubBasketService>();
builder.Services.AddHostedService<BasketRefreshJob>();

var app = builder.Build();

app.Services.LogConfigurationPosture(
    "hqqq-reference-data",
    app.Logger,
    "Kafka", "Redis", "Timescale");

app.MapHqqqHealthEndpoints();

app.MapBasketEndpoints();

// TODO: Phase 2B — replace SeedFileBasketRepository with Timescale-backed implementation
// TODO: Phase 2B — wire real basket refresh from issuer feeds (corp actions, NAV recalibration)

app.Run();

public partial class Program { }
