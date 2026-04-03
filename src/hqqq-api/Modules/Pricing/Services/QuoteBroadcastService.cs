using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Hqqq.Api.Configuration;
using Hqqq.Api.Hubs;

namespace Hqqq.Api.Modules.Pricing.Services;

/// <summary>
/// Background service that runs a 1-second broadcast loop:
/// bootstrap → activate pending → compute quote → record series → SignalR push.
/// </summary>
public sealed class QuoteBroadcastService : BackgroundService
{
    private readonly PricingEngine _engine;
    private readonly IHubContext<MarketHub> _hubContext;
    private readonly PricingOptions _options;
    private readonly ILogger<QuoteBroadcastService> _logger;

    public QuoteBroadcastService(
        PricingEngine engine,
        IHubContext<MarketHub> hubContext,
        IOptions<PricingOptions> options,
        ILogger<QuoteBroadcastService> logger)
    {
        _engine = engine;
        _hubContext = hubContext;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Quote broadcast service starting");

        // Allow basket + market-data services time to initialize
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        await _engine.InitializeAsync(stoppingToken);

        var interval = TimeSpan.FromMilliseconds(_options.QuoteBroadcastIntervalMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_engine.IsInitialized)
                {
                    var bootstrapped = await _engine.TryBootstrapAsync(stoppingToken);
                    if (bootstrapped)
                        _logger.LogInformation("Pricing engine bootstrapped successfully");
                }
                else
                {
                    await _engine.TryActivatePendingAsync(stoppingToken);
                }

                var quote = _engine.ComputeQuote();
                if (quote is not null)
                {
                    _engine.RecordSeriesPoint(quote);
                    await _hubContext.Clients.All
                        .SendAsync("QuoteUpdate", quote, stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Quote broadcast cycle error");
            }

            await Task.Delay(interval, stoppingToken);
        }

        _logger.LogInformation("Quote broadcast service stopped");
    }
}
