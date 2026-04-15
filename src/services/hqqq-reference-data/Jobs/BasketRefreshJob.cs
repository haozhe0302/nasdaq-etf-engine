namespace Hqqq.ReferenceData.Jobs;

/// <summary>
/// Placeholder for the scheduled basket-refresh background job.
/// Will be implemented in Phase 2B with data-source adapters and activation scheduling.
/// </summary>
public sealed class BasketRefreshJob(ILogger<BasketRefreshJob> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("BasketRefreshJob: not yet implemented (Phase 2B)");
        return Task.CompletedTask;
    }
}
