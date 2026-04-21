using Hqqq.Gateway.Configuration;
using Hqqq.Infrastructure.Redis;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Hqqq.Gateway.Services.Realtime;

/// <summary>
/// Phase 2D2 — subscribes to <see cref="RedisKeys.QuoteUpdateChannel"/>
/// and forwards each payload to <see cref="QuoteUpdateBroadcaster"/>, which
/// owns deserialization, validation, and SignalR fan-out.
///
/// Phase 2-hotfix — Redis pub/sub unavailability is a <em>degraded</em>
/// condition, not a fatal one. Subscription failures are caught and
/// retried with bounded exponential backoff + jitter so the host keeps
/// running, <c>/api/system/health</c> remains servable, and realtime
/// recovers automatically once Redis is reachable again. This preserves
/// the default <c>BackgroundServiceExceptionBehavior.StopHost</c> for
/// other, genuinely fatal background-service failures instead of
/// loosening it globally.
/// </summary>
public sealed class QuoteUpdateSubscriber : BackgroundService
{
    private readonly IRedisQuoteUpdateChannel _channel;
    private readonly QuoteUpdateBroadcaster _broadcaster;
    private readonly ILogger<QuoteUpdateSubscriber> _logger;
    private readonly GatewayRealtimeOptions _options;

    public QuoteUpdateSubscriber(
        IRedisQuoteUpdateChannel channel,
        QuoteUpdateBroadcaster broadcaster,
        ILogger<QuoteUpdateSubscriber> logger,
        IOptions<GatewayRealtimeOptions> options)
    {
        _channel = channel;
        _broadcaster = broadcaster;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            // Realtime is explicitly disabled (e.g. test host, offline smoke
            // run). Exit cleanly without touching Redis so the host has no
            // implicit pub/sub dependency at all.
            _logger.LogInformation(
                "QuoteUpdateSubscriber is disabled via Gateway:Realtime:Enabled=false; " +
                "gateway will not subscribe to Redis channel {Channel}.",
                RedisKeys.QuoteUpdateChannel);
            return;
        }

        var initialDelay = NormalizeDelay(
            _options.InitialRetryDelayMs, defaultMs: 1_000);
        var maxDelay = Math.Max(
            initialDelay,
            NormalizeDelay(_options.MaxRetryDelayMs, defaultMs: 30_000));
        var delayMs = initialDelay;

        // Per-instance RNG for jitter — a few hundred ms of spread is
        // plenty to avoid synchronized retries across replicas on a
        // flapping Redis.
        var jitter = new Random();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _channel.SubscribeAsync(DispatchAsync, stoppingToken)
                    .ConfigureAwait(false);

                _logger.LogInformation(
                    "QuoteUpdateSubscriber listening on Redis channel {Channel}",
                    RedisKeys.QuoteUpdateChannel);

                // Healthy steady state: StackExchange.Redis transparently
                // handles multiplexer reconnects, so a successful subscribe
                // only needs to wait for shutdown.
                delayMs = initialDelay;
                await Task.Delay(Timeout.Infinite, stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (RedisConnectionException ex)
            {
                LogDegraded(ex, delayMs);
            }
            catch (RedisTimeoutException ex)
            {
                LogDegraded(ex, delayMs);
            }
            catch (RedisException ex)
            {
                // Any other transient transport-layer Redis failure is
                // treated the same way: log, back off, retry — never kill
                // the host.
                LogDegraded(ex, delayMs);
            }

            // Best-effort cleanup so the next iteration sees a clean slate.
            try
            {
                await _channel.UnsubscribeAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogDebug(cleanupEx,
                    "QuoteUpdateSubscriber unsubscribe after failed attempt threw; " +
                    "continuing retry loop");
            }

            try
            {
                var jitterMs = jitter.Next(0, Math.Max(1, delayMs / 4));
                await Task.Delay(
                    TimeSpan.FromMilliseconds(delayMs + jitterMs),
                    stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            delayMs = Math.Min(maxDelay, Math.Max(initialDelay, delayMs * 2));
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _channel.UnsubscribeAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "QuoteUpdateSubscriber failed to unsubscribe cleanly from {Channel}",
                RedisKeys.QuoteUpdateChannel);
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task DispatchAsync(string payload, CancellationToken ct)
        => _broadcaster.DispatchAsync(payload, ct);

    private void LogDegraded(Exception ex, int delayMs)
    {
        _logger.LogWarning(ex,
            "QuoteUpdateSubscriber could not subscribe to Redis channel {Channel}; " +
            "gateway realtime is degraded. Retrying in ~{DelayMs}ms.",
            RedisKeys.QuoteUpdateChannel, delayMs);
    }

    private static int NormalizeDelay(int configured, int defaultMs)
        => configured > 0 ? configured : defaultMs;
}
