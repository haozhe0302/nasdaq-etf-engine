using Hqqq.Ingress;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHostedService<IngressWorker>();

var host = builder.Build();
host.Run();
