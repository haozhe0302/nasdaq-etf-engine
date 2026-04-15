using Hqqq.QuoteEngine;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHostedService<QuoteEngineWorker>();

var host = builder.Build();
host.Run();
