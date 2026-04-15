namespace Hqqq.Ingress.State;

/// <summary>
/// Thread-safe ingestion state tracker.
/// Read by the worker for logging; future: exposed via a health endpoint.
/// </summary>
public sealed class IngestionState
{
    private volatile bool _isRunning;
    private volatile bool _isWebSocketConnected;
    private volatile bool _isFallbackActive;
    private long _lastActivityTicks;
    private long _ticksIngested;

    public bool IsRunning => _isRunning;
    public bool IsWebSocketConnected => _isWebSocketConnected;
    public bool IsFallbackActive => _isFallbackActive;
    public long TicksIngested => Interlocked.Read(ref _ticksIngested);

    public DateTimeOffset? LastActivityUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastActivityTicks);
            return ticks > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
        }
    }

    public void SetRunning(bool value) => _isRunning = value;
    public void SetWebSocketConnected(bool value) => _isWebSocketConnected = value;
    public void SetFallbackActive(bool value) => _isFallbackActive = value;

    public void RecordTick()
    {
        Interlocked.Increment(ref _ticksIngested);
        Interlocked.Exchange(ref _lastActivityTicks, DateTimeOffset.UtcNow.UtcTicks);
    }
}
