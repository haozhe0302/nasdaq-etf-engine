namespace Hqqq.Analytics;

public sealed class AnalyticsWorker : BackgroundService
{
    private readonly ILogger<AnalyticsWorker> _logger;

    public AnalyticsWorker(ILogger<AnalyticsWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AnalyticsWorker starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        _logger.LogInformation("AnalyticsWorker stopping");
    }
}
