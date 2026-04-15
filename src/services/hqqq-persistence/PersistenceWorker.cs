namespace Hqqq.Persistence;

public sealed class PersistenceWorker : BackgroundService
{
    private readonly ILogger<PersistenceWorker> _logger;

    public PersistenceWorker(ILogger<PersistenceWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PersistenceWorker starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        _logger.LogInformation("PersistenceWorker stopping");
    }
}
