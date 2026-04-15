using Hqqq.Ingress.Clients;
using Hqqq.Ingress.Configuration;
using Hqqq.Ingress.Publishing;
using Hqqq.Ingress.State;
using Hqqq.Ingress.Workers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<TiingoOptions>(
    builder.Configuration.GetSection("Tiingo"));

builder.Services.AddSingleton<ITiingoStreamClient, StubTiingoStreamClient>();
builder.Services.AddSingleton<ITiingoSnapshotClient, StubTiingoSnapshotClient>();
builder.Services.AddSingleton<ITickPublisher, LoggingTickPublisher>();
builder.Services.AddSingleton<IngestionState>();
builder.Services.AddHostedService<TiingoIngressWorker>();

var host = builder.Build();
host.Run();
