namespace Hqqq.Analytics.Services;

/// <summary>
/// Common shape for a one-shot analytics job. The dispatcher selects the
/// correct implementation based on <c>Analytics:Mode</c> and is responsible
/// for signalling <see cref="IHostApplicationLifetime.StopApplication"/> so
/// the host exits cleanly after the job completes.
/// </summary>
public interface IReportJob
{
    /// <summary>
    /// Stable identifier that matches an <c>Analytics:Mode</c> string.
    /// </summary>
    string Mode { get; }

    /// <summary>
    /// Runs the job to completion. Implementations must not block on
    /// synchronous I/O and must honour the cancellation token.
    /// </summary>
    Task RunAsync(CancellationToken ct);
}
