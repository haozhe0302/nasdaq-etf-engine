namespace Hqqq.QuoteEngine;

public sealed class QuoteEngineWorker : BackgroundService
{
    private readonly ILogger<QuoteEngineWorker> _logger;

    public QuoteEngineWorker(ILogger<QuoteEngineWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("QuoteEngineWorker starting");

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        _logger.LogInformation("QuoteEngineWorker stopping");
    }
}
