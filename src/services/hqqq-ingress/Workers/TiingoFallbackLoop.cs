using Hqqq.Ingress.Clients;
using Hqqq.Ingress.Configuration;
using Hqqq.Ingress.Publishing;
using Hqqq.Ingress.State;
using Microsoft.Extensions.Options;

namespace Hqqq.Ingress.Workers;

/// <summary>
/// Production REST fallback for the Tiingo IEX websocket. Runs in parallel
/// with <see cref="TiingoIngressWorker"/>'s websocket loop and keeps the
/// downstream Kafka pipeline (<c>market.raw_ticks.v1</c> +
/// <c>market.latest_by_symbol.v1</c>) fed whenever the websocket is
/// disconnected, stale, or repeatedly failing.
/// </summary>
/// <remarks>
/// <para>
/// Activation policy:
/// <list type="bullet">
///   <item><b>Active</b> — websocket is not connected, OR
///         <see cref="IngestionState.LastActivityUtc"/> is older than
///         <see cref="TiingoOptions.FallbackActivationStalenessSeconds"/>
///         (capped at <see cref="TiingoOptions.StaleAfterSeconds"/>).</item>
///   <item><b>Standby</b> — websocket is connected and producing fresh
///         ticks. The loop still wakes every
///         <see cref="TiingoOptions.RestPollingIntervalSeconds"/> to
///         re-evaluate, but does not call Tiingo.</item>
/// </list>
/// On every active poll the loop:
/// </para>
/// <list type="number">
///   <item>Reads the active symbol set from
///         <see cref="BasketSubscriptionCoordinator.CurrentAppliedSymbols"/>.</item>
///   <item>Calls <see cref="ITiingoSnapshotClient.FetchSnapshotsAsync"/>
///         once for the whole set (batch produce — no per-symbol fan-out).</item>
///   <item>Publishes the resulting <c>RawTickV1</c> events through the
///         shared <see cref="ITickPublisher"/> path so consumers see the
///         exact same wire shape they get from the websocket.</item>
///   <item>Updates <see cref="IngestionState"/>:
///         <c>SetFallbackActive(true)</c> for the duration of the poll,
///         <c>RecordPublishedTicks(n)</c> on success, and
///         <c>RecordFallbackPollFailure(msg)</c> on any non-cancellation
///         exception.</item>
/// </list>
/// <para>
/// The loop is non-throwing — transient REST failures log and continue, so
/// a misbehaving Tiingo endpoint cannot crash the ingress process. All
/// lifecycle events are tagged with <c>[fallback:*]</c> for grep-friendly
/// diagnostics.
/// </para>
/// </remarks>
public sealed class TiingoFallbackLoop
{
    private readonly ITiingoSnapshotClient _snapshotClient;
    private readonly ITickPublisher _publisher;
    private readonly IngestionState _state;
    private readonly BasketSubscriptionCoordinator _coordinator;
    private readonly TiingoOptions _options;
    private readonly ILogger<TiingoFallbackLoop> _logger;
    private readonly TimeProvider _clock;

    public TiingoFallbackLoop(
        ITiingoSnapshotClient snapshotClient,
        ITickPublisher publisher,
        IngestionState state,
        BasketSubscriptionCoordinator coordinator,
        IOptions<TiingoOptions> options,
        ILogger<TiingoFallbackLoop> logger,
        TimeProvider? clock = null)
    {
        _snapshotClient = snapshotClient;
        _publisher = publisher;
        _state = state;
        _coordinator = coordinator;
        _options = options.Value;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>
    /// Runs the fallback loop until <paramref name="ct"/> fires. Never
    /// throws — internal exceptions are logged and folded into
    /// <see cref="IngestionState.RecordFallbackPollFailure"/>.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        var intervalSeconds = Math.Max(1, _options.RestPollingIntervalSeconds);
        _logger.LogInformation(
            "[fallback:start] Tiingo REST fallback loop running every {Interval}s", intervalSeconds);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await PollOnceAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _state.RecordFallbackPollFailure($"fallback loop error: {ex.Message}");
                    _logger.LogWarning(ex,
                        "[fallback:error] unexpected error in fallback loop tick — continuing");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
            }
        }
        finally
        {
            _state.SetFallbackActive(false);
            _logger.LogInformation("[fallback:stop] Tiingo REST fallback loop exiting");
        }
    }

    /// <summary>
    /// One iteration of the fallback decision: evaluate whether the
    /// websocket is delivering fresh ticks, and if not, fetch + publish
    /// a REST snapshot. Returns the decision so callers (and tests) can
    /// assert on it. Public so tests can drive the loop deterministically
    /// without spinning the timer.
    /// </summary>
    public async Task<FallbackTickOutcome> PollOnceAsync(CancellationToken ct)
    {
        var needed = ShouldActivateFallback();
        if (!needed)
        {
            if (_state.IsFallbackActive)
            {
                _logger.LogInformation(
                    "[fallback:stop] websocket healthy again (lastActivity={Last}), deactivating fallback",
                    _state.LastActivityUtc?.ToString("o") ?? "<none>");
            }
            _state.SetFallbackActive(false);
            return FallbackTickOutcome.Standby;
        }

        var symbols = _coordinator.CurrentAppliedSymbols;
        if (symbols.Count == 0)
        {
            // No basket yet — keep the loop in "armed but idle" state so the
            // health probe can still report the WS as not delivering, but
            // don't fire empty REST requests.
            _state.SetFallbackActive(true);
            _logger.LogDebug(
                "[fallback:poll] websocket not delivering but no active symbols yet — skipping REST poll");
            return FallbackTickOutcome.SkippedNoSymbols;
        }

        _state.SetFallbackActive(true);
        _logger.LogInformation(
            "[fallback:poll] websocket not delivering (wsConnected={Connected} lastActivity={Last}); polling REST for {Count} symbols",
            _state.IsWebSocketConnected,
            _state.LastActivityUtc?.ToString("o") ?? "<none>",
            symbols.Count);

        IReadOnlyList<Hqqq.Contracts.Events.RawTickV1> snapshot;
        try
        {
            snapshot = await _snapshotClient.FetchSnapshotsAsync(symbols, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _state.RecordFallbackPollFailure($"REST fetch failed: {ex.Message}");
            _logger.LogWarning(ex,
                "[fallback:error] REST snapshot fetch failed for {Count} symbols", symbols.Count);
            return FallbackTickOutcome.Failed;
        }

        if (snapshot.Count == 0)
        {
            _state.RecordFallbackPollFailure(
                $"REST snapshot returned 0 rows for {symbols.Count} symbols");
            _logger.LogWarning(
                "[fallback:error] REST snapshot returned 0 rows for {Count} symbols", symbols.Count);
            return FallbackTickOutcome.Failed;
        }

        try
        {
            await _publisher.PublishBatchAsync(snapshot, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _state.RecordFallbackPollFailure($"Kafka publish failed: {ex.Message}");
            _logger.LogWarning(ex,
                "[fallback:error] publish failed for {Count} REST snapshot ticks", snapshot.Count);
            return FallbackTickOutcome.Failed;
        }

        _state.RecordPublishedTicks(snapshot.Count);
        _state.RecordFallbackPollSuccess();
        _logger.LogInformation(
            "[fallback:published] {Count} REST snapshot ticks for active basket", snapshot.Count);
        return FallbackTickOutcome.Published;
    }

    /// <summary>
    /// Encodes the "does the websocket appear to be delivering fresh ticks
    /// right now?" rule used by both the loop and the health check. Exposed
    /// internally so tests can lock down the boundary conditions.
    /// </summary>
    internal bool ShouldActivateFallback()
    {
        if (!_state.IsWebSocketConnected) return true;

        var stalenessCap = Math.Max(1, _options.StaleAfterSeconds);
        var threshold = Math.Clamp(
            Math.Max(1, _options.FallbackActivationStalenessSeconds), 1, stalenessCap);
        var last = _state.LastActivityUtc;
        if (last is null) return true; // connected but never delivered a frame
        var age = _clock.GetUtcNow() - last.Value;
        return age > TimeSpan.FromSeconds(threshold);
    }
}

/// <summary>Outcome of a single <see cref="TiingoFallbackLoop.PollOnceAsync"/>.</summary>
public enum FallbackTickOutcome
{
    /// <summary>Websocket is delivering — no REST poll required.</summary>
    Standby,

    /// <summary>Fallback armed but skipped because there are no symbols subscribed yet.</summary>
    SkippedNoSymbols,

    /// <summary>REST snapshot fetched and successfully published to Kafka.</summary>
    Published,

    /// <summary>REST fetch or publish failed; <see cref="IngestionState.LastError"/> carries the message.</summary>
    Failed,
}
