using dotenv.net;
using Prometheus;
using Hqqq.Api.Configuration;
using Hqqq.Api.Hubs;
using Hqqq.Api.Modules.Basket;
using Hqqq.Api.Modules.Benchmark;
using Hqqq.Api.Modules.History;
using Hqqq.Api.Modules.MarketData;
using Hqqq.Api.Modules.Pricing;
using Hqqq.Api.Modules.System;

DotEnv.Load(options: new DotEnvOptions(probeForEnv: true, probeLevelsToSearch: 5));

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddFlatEnvironmentVariables();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();

builder.Services.Configure<TiingoOptions>(
    builder.Configuration.GetSection(TiingoOptions.SectionName));
builder.Services.Configure<BasketOptions>(
    builder.Configuration.GetSection(BasketOptions.SectionName));
builder.Services.Configure<PricingOptions>(
    builder.Configuration.GetSection(PricingOptions.SectionName));
builder.Services.Configure<FeatureOptions>(
    builder.Configuration.GetSection(FeatureOptions.SectionName));
builder.Services.Configure<RecordingOptions>(
    builder.Configuration.GetSection(RecordingOptions.SectionName));

builder.Services
    .AddBasketModule()
    .AddMarketDataModule()
    .AddPricingModule()
    .AddSystemModule()
    .AddBenchmarkModule()
    .AddHistoryModule();

var allowedOrigins =
    builder.Configuration["HQQQ_ALLOWED_ORIGINS"]?
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    ?? new[] { "http://localhost:5173", "http://localhost:3000" };

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseHttpsRedirection();

app.MapBasketEndpoints();
app.MapMarketDataEndpoints();
app.MapPricingEndpoints();
app.MapSystemEndpoints();
app.MapHistoryEndpoints();
app.MapMetrics();

app.MapHub<MarketHub>("/hubs/market");

app.Run();
