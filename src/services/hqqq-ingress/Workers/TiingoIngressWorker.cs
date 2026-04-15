using Microsoft.Extensions.Options;
using Hqqq.Ingress.Clients;
using Hqqq.Ingress.Configuration;
using Hqqq.Ingress.Publishing;
using Hqqq.Ingress.State;

namespace Hqqq.Ingress.Workers;

/// <summary>
/// Main hosted worker that orchestrates the Tiingo ingestion pipeline.
/// Currently a placeholder that logs lifecycle stages and idles.
/// </summary>
public sealed class TiingoIngressWorker(
    ITiingoStreamClient streamClient,
    ITiingoSnapshotClient snapshotClient,
    ITickPublisher publisher,
    IngestionState state,
    IOptions<TiingoOptions> options,
    ILogger<TiingoIngressWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TiingoIngressWorker starting");
        state.SetRunning(true);

        try
        {
            var apiKey = options.Value.ApiKey;
            if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Contains("YOUR_"))
            {
                logger.LogWarning(
                    "Tiingo API key not configured — ingestion is disabled. " +
                    "Set Tiingo:ApiKey in configuration to enable.");
                return;
            }

            logger.LogInformation("Waiting for symbol subscription (Phase 2B)");

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
        finally
        {
            state.SetRunning(false);
            logger.LogInformation("TiingoIngressWorker stopping");
        }
    }
}
