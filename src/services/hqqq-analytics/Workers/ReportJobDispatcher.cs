using Hqqq.Analytics.Options;
using Hqqq.Analytics.Services;
using Microsoft.Extensions.Options;

namespace Hqqq.Analytics.Workers;

/// <summary>
/// Single <see cref="IHostedService"/> entry point for the analytics host.
/// Selects the correct <see cref="IReportJob"/> based on <c>Analytics:Mode</c>,
/// runs it to completion on a background task so <see cref="StartAsync"/>
/// returns immediately, then signals the host to stop.
/// </summary>
/// <remarks>
/// Only <see cref="AnalyticsOptions.ReportMode"/> is implemented in C4.
/// Unknown modes produce a clear "not implemented" error and still stop
/// the host with a non-zero exit code so scripted callers notice.
/// </remarks>
public sealed class ReportJobDispatcher : IHostedService
{
    private readonly IEnumerable<IReportJob> _jobs;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly AnalyticsOptions _options;
    private readonly ILogger<ReportJobDispatcher> _logger;
    private Task? _runTask;

    public ReportJobDispatcher(
        IEnumerable<IReportJob> jobs,
        IHostApplicationLifetime lifetime,
        IOptions<AnalyticsOptions> options,
        ILogger<ReportJobDispatcher> logger)
    {
        _jobs = jobs;
        _lifetime = lifetime;
        _options = options.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _runTask = Task.Run(() => DispatchAsync(_lifetime.ApplicationStopping), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_runTask is null) return;
        try
        {
            await _runTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task DispatchAsync(CancellationToken ct)
    {
        try
        {
            var job = _jobs.FirstOrDefault(j => string.Equals(j.Mode, _options.Mode, StringComparison.OrdinalIgnoreCase));
            if (job is null)
            {
                _logger.LogError(
                    "Analytics:Mode='{Mode}' is not implemented in C4. Supported modes: [{Supported}].",
                    _options.Mode,
                    string.Join(", ", _jobs.Select(j => j.Mode)));
                Environment.ExitCode = 2;
                return;
            }

            _logger.LogInformation("Dispatching analytics job: mode={Mode}", job.Mode);
            await job.RunAsync(ct).ConfigureAwait(false);
            _logger.LogInformation("Analytics job completed: mode={Mode}", job.Mode);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("Analytics job cancelled during shutdown");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Analytics job failed");
            Environment.ExitCode = 1;
        }
        finally
        {
            _lifetime.StopApplication();
        }
    }
}
