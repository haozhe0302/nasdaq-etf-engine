namespace Hqqq.Ingress.State;

/// <summary>
/// Thread-safe ingestion state tracker. Read by the worker for logging,
/// by <see cref="Health.IngressUpstreamHealthCheck"/> for the
/// <c>/healthz/ready</c> payload, and by <see cref="Metrics.IngressMetrics"/>
/// to back the observable gauges scraped on <c>/metrics</c>.
/// </summary>
/// <remarks>
/// Two distinct counters are tracked:
/// <list type="bullet">
///   <item><see cref="TicksIngested"/> — incremented when a frame is
///         decoded off the Tiingo websocket (provider-side activity).</item>
///   <item><see cref="PublishedTickCount"/> — incremented only after a
///         successful Kafka produce. This is the runtime tick-flow signal
///         that smoke proofs sample to verify ingress is actually
///         publishing, not just receiving.</item>
/// </list>
/// </remarks>
public sealed class IngestionState
{
    private readonly object _errorLock = new();
    private volatile bool _isRunning;
    private volatile bool _isWebSocketConnected;
    private volatile bool _isFallbackActive;
    private long _lastActivityTicks;
    private long _ticksIngested;
    private long _ticksPublished;
    private long _lastPublishedTicks;
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

    /// <summary>Number of decoded frames received from the upstream provider.</summary>
    public long TicksIngested => Interlocked.Read(ref _ticksIngested);

    /// <summary>
    /// Number of ticks successfully published to Kafka. Smoke proofs
    /// sample this counter twice across a window to confirm live tick
    /// flow rather than inferring it from downstream side effects.
    /// </summary>
    public long PublishedTickCount => Interlocked.Read(ref _ticksPublished);

    public DateTimeOffset? LastActivityUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastActivityTicks);
            return ticks > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;
        }
    }

    /// <summary>
    /// UTC timestamp of the most recent successful Kafka publish, or
    /// <c>null</c> if nothing has been published yet.
    /// </summary>
    public DateTimeOffset? LastPublishedTickUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _lastPublishedTicks);
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
    /// Records that a single tick was successfully produced to Kafka.
    /// Updates both the running counter and the last-publish timestamp;
    /// safe to call from any thread.
    /// </summary>
    public void RecordPublishedTick()
    {
        Interlocked.Increment(ref _ticksPublished);
        Interlocked.Exchange(ref _lastPublishedTicks, DateTimeOffset.UtcNow.UtcTicks);
    }

    /// <summary>
    /// Bulk variant for the snapshot warmup batch. Increments by
    /// <paramref name="count"/> in one go and stamps the latest
    /// publish timestamp once.
    /// </summary>
    public void RecordPublishedTicks(int count)
    {
        if (count <= 0) return;
        Interlocked.Add(ref _ticksPublished, count);
        Interlocked.Exchange(ref _lastPublishedTicks, DateTimeOffset.UtcNow.UtcTicks);
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
