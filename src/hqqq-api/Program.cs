using Hqqq.Api.Modules.Basket;
using Hqqq.Api.Modules.MarketData;
using Hqqq.Api.Modules.Pricing;
using Hqqq.Api.Modules.System;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services
    .AddBasketModule()
    .AddMarketDataModule()
    .AddPricingModule()
    .AddSystemModule();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapBasketEndpoints();
app.MapMarketDataEndpoints();
app.MapPricingEndpoints();
app.MapSystemEndpoints();

app.Run();
