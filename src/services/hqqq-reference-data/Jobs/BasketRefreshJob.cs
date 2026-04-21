using Hqqq.ReferenceData.Configuration;
using Hqqq.ReferenceData.Services;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Jobs;

/// <summary>
/// Single background job that owns the active-basket lifecycle in both
/// operating modes:
/// <list type="bullet">
///   <item>runs one refresh on startup (bounded by <c>Refresh.StartupMaxWaitSeconds</c>);</item>
///   <item>fires a real refresh every <c>Refresh.IntervalSeconds</c>;</item>
///   <item>re-publishes the current active basket every <c>Refresh.RepublishIntervalSeconds</c> (even when unchanged) so late / restarted consumers hydrate from the compacted topic without operator action.</item>
/// </list>
/// All timers are defensive: failures are logged and retried on the next
/// tick; the service never crashes on a transient Kafka or source issue.
/// </summary>
public sealed class BasketRefreshJob : BackgroundService
{
    private readonly BasketRefreshPipeline _pipeline;
    private readonly RefreshOptions _options;
    private readonly ILogger<BasketRefreshJob> _logger;

    public BasketRefreshJob(
        BasketRefreshPipeline pipeline,
        IOptions<ReferenceDataOptions> options,
        ILogger<BasketRefreshJob> logger)
    {
        _pipeline = pipeline;
        _options = options.Value.Refresh;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "BasketRefreshJob starting — refreshInterval={Refresh}s republishInterval={Republish}s startupMaxWait={Startup}s",
            _options.IntervalSeconds, _options.RepublishIntervalSeconds, _options.StartupMaxWaitSeconds);

        await RunStartupRefreshAsync(stoppingToken).ConfigureAwait(false);

        var refreshInterval = TimeSpan.FromSeconds(Math.Max(1, _options.IntervalSeconds));
        var republishInterval = TimeSpan.FromSeconds(Math.Max(1, _options.RepublishIntervalSeconds));

        using var refreshTimer = _options.IntervalSeconds > 0 ? new PeriodicTimer(refreshInterval) : null;
        using var republishTimer = _options.RepublishIntervalSeconds > 0 ? new PeriodicTimer(republishInterval) : null;

        var refreshTask = refreshTimer is null ? null : RunLoopAsync(refreshTimer, RefreshTickAsync, stoppingToken);
        var republishTask = republishTimer is null ? null : RunLoopAsync(republishTimer, RepublishTickAsync, stoppingToken);

        var loops = new[] { refreshTask, republishTask }
            .Where(t => t is not null)
            .Select(t => t!)
            .ToArray();

        if (loops.Length == 0)
        {
            _logger.LogInformation("BasketRefreshJob: both timers disabled; exiting after startup refresh");
            return;
        }

        try
        {
            await Task.WhenAll(loops).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async Task RunStartupRefreshAsync(CancellationToken stoppingToken)
    {
        var startupTimeout = TimeSpan.FromSeconds(Math.Max(1, _options.StartupMaxWaitSeconds));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        cts.CancelAfter(startupTimeout);

        try
        {
            var result = await _pipeline.RefreshAsync(cts.Token).ConfigureAwait(false);
            if (result.Success)
            {
                _logger.LogInformation(
                    "BasketRefreshJob: startup refresh complete — changed={Changed} source={Source} fingerprint={Fingerprint} count={Count}",
                    result.Changed, result.Source, result.Fingerprint, result.ConstituentCount);
            }
            else
            {
                _logger.LogError(
                    "BasketRefreshJob: startup refresh failed — error={Error}; periodic refresh will retry",
                    result.Error);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "BasketRefreshJob: startup refresh exceeded {Timeout}s budget; periodic refresh will continue",
                startupTimeout.TotalSeconds);
        }
    }

    private static async Task RunLoopAsync(PeriodicTimer timer, Func<CancellationToken, Task> tick, CancellationToken stoppingToken)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await tick(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshTickAsync(CancellationToken ct)
    {
        try
        {
            var result = await _pipeline.RefreshAsync(ct).ConfigureAwait(false);
            if (result.Success)
            {
                _logger.LogInformation(
                    "Refresh tick — changed={Changed} source={Source} fingerprint={Fingerprint} count={Count}",
                    result.Changed, result.Source, result.Fingerprint, result.ConstituentCount);
            }
            else
            {
                _logger.LogWarning("Refresh tick failed — {Error}", result.Error);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Refresh tick threw; will retry on next interval");
        }
    }

    private async Task RepublishTickAsync(CancellationToken ct)
    {
        try
        {
            var result = await _pipeline.RepublishCurrentAsync(ct).ConfigureAwait(false);
            if (result.Success)
            {
                _logger.LogDebug(
                    "Republish tick — source={Source} fingerprint={Fingerprint} count={Count}",
                    result.Source, result.Fingerprint, result.ConstituentCount);
            }
            else
            {
                _logger.LogDebug("Republish tick skipped — {Error}", result.Error);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Republish tick threw; will retry on next interval");
        }
    }
}
