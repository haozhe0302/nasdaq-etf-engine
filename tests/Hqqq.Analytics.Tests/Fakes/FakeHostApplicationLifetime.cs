using Microsoft.Extensions.Hosting;

namespace Hqqq.Analytics.Tests.Fakes;

internal sealed class FakeHostApplicationLifetime : IHostApplicationLifetime
{
    private readonly CancellationTokenSource _started = new();
    private readonly CancellationTokenSource _stopping = new();
    private readonly CancellationTokenSource _stopped = new();

    public CancellationToken ApplicationStarted => _started.Token;
    public CancellationToken ApplicationStopping => _stopping.Token;
    public CancellationToken ApplicationStopped => _stopped.Token;

    public bool StopApplicationCalled { get; private set; }

    public void StopApplication()
    {
        StopApplicationCalled = true;
        _stopping.Cancel();
        _stopped.Cancel();
    }

    public void SignalStarted() => _started.Cancel();
}
