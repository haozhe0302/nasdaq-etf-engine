namespace Hqqq.Ingress.State;

/// <summary>
/// Thread-safe ingestion state tracker. Read by the worker for logging
/// and by <see cref="Health.IngressUpstreamHealthCheck"/> for the
/// <c>/healthz/ready</c> payload.
/// </summary>
public sealed class IngestionState
{
    private readonly object _errorLock = new();
    private volatile bool _isRunning;
    private volatile bool _isWebSocketConnected;
    private volatile bool _isFallbackActive;
    private long _lastActivityTicks;
    private long _ticksIngested;
    private string? _lastError;
    private DateTimeOffset? _lastErrorAtUtc;

    public bool IsRunning => _isRunning;

    /// <summary>Whether the upstream websocket is currently connected.</summary>
    public bool IsWebSocketConnected => _isWebSocketConnected;

    /// <summary>
    /// Alias for <see cref="IsWebSocketConnected"/> kept for the health-
    /// check payload (the term "upstream" reads better than the transport
    /// name in operator-facing JSON).
    /// </summary>
    public bool IsUpstreamConnected => _isWebSocketConnected;

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

    /// <summary>Last upstream error message, or <c>null</c> if no error has been observed.</summary>
    public string? LastError
    {
        get { lock (_errorLock) { return _lastError; } }
    }

    /// <summary>UTC timestamp of the last recorded error.</summary>
    public DateTimeOffset? LastErrorAtUtc
    {
        get { lock (_errorLock) { return _lastErrorAtUtc; } }
    }

    public void SetRunning(bool value) => _isRunning = value;
    public void SetWebSocketConnected(bool value) => _isWebSocketConnected = value;
    public void SetFallbackActive(bool value) => _isFallbackActive = value;

    public void RecordTick()
    {
        Interlocked.Increment(ref _ticksIngested);
        Interlocked.Exchange(ref _lastActivityTicks, DateTimeOffset.UtcNow.UtcTicks);
    }

    /// <summary>
    /// Stamps the last upstream error message + timestamp. Safe to call
    /// from any thread; the most recent call wins.
    /// </summary>
    public void RecordError(string message)
    {
        lock (_errorLock)
        {
            _lastError = message;
            _lastErrorAtUtc = DateTimeOffset.UtcNow;
        }
    }
}
