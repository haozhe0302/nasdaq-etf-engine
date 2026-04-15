using Hqqq.Gateway.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok("ok"));

app.MapGet("/api/quote", () =>
    Results.Json(new { status = "not_wired", message = "Gateway not yet connected to Redis" },
        statusCode: 503));

app.MapHub<MarketHub>("/hubs/market");

app.Run();
