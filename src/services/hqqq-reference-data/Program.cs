using Hqqq.ReferenceData.Endpoints;
using Hqqq.ReferenceData.Jobs;
using Hqqq.ReferenceData.Repositories;
using Hqqq.ReferenceData.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IBasketRepository, InMemoryBasketRepository>();
builder.Services.AddSingleton<IBasketService, StubBasketService>();
builder.Services.AddHostedService<BasketRefreshJob>();

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok("ok"));
app.MapBasketEndpoints();

app.Run();
