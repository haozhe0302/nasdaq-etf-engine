using Hqqq.Persistence;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHostedService<PersistenceWorker>();

var host = builder.Build();
host.Run();
