namespace Hqqq.Ingress;

public sealed class IngressWorker : BackgroundService
{
    private readonly ILogger<IngressWorker> _logger;

    public IngressWorker(ILogger<IngressWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IngressWorker starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        _logger.LogInformation("IngressWorker stopping");
    }
}
