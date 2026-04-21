using Hqqq.Ingress.Clients;
using Hqqq.Ingress.Configuration;
using Hqqq.Ingress.Publishing;
using Hqqq.Ingress.State;
using Microsoft.Extensions.Options;

namespace Hqqq.Ingress.Workers;

/// <summary>
/// Hosted worker that orchestrates Tiingo ingestion. Phase 2 ingress has a
/// single self-sufficient runtime path: validate Tiingo:ApiKey → wait for
/// the first basket (or fall back to <c>Tiingo:Symbols</c> override) →
/// snapshot warmup → websocket loop with bounded exponential backoff.
/// Mid-session basket updates are applied through
/// <see cref="BasketSubscriptionCoordinator"/>.
/// </summary>
public sealed class TiingoIngressWorker : BackgroundService
{
    private static readonly string[] PlaceholderMarkers = new[] { "<set", "your_", "changeme", "replace_me" };

    private readonly ITiingoStreamClient _streamClient;
    private readonly ITiingoSnapshotClient _snapshotClient;
    private readonly ITickPublisher _publisher;
    private readonly IngestionState _state;
    private readonly ActiveSymbolUniverse _universe;
    private readonly BasketSubscriptionCoordinator _coordinator;
    private readonly TiingoOptions _tiingoOptions;
    private readonly IngressBasketOptions _basketOptions;
    private readonly ILogger<TiingoIngressWorker> _logger;

    public TiingoIngressWorker(
        ITiingoStreamClient streamClient,
        ITiingoSnapshotClient snapshotClient,
        ITickPublisher publisher,
        IngestionState state,
        ActiveSymbolUniverse universe,
        BasketSubscriptionCoordinator coordinator,
        IOptions<TiingoOptions> tiingoOptions,
        IOptions<IngressBasketOptions> basketOptions,
        ILogger<TiingoIngressWorker> logger)
    {
        _streamClient = streamClient;
        _snapshotClient = snapshotClient;
        _publisher = publisher;
        _state = state;
        _universe = universe;
        _coordinator = coordinator;
        _tiingoOptions = tiingoOptions.Value;
        _basketOptions = basketOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Validates preconditions before the host advertises as started.
    /// Throws so the process exits and the orchestrator restarts with
    /// the operator-visible error — Phase 2 does not have a silent
    /// degraded mode here.
    /// </summary>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (!HasUsableApiKey(_tiingoOptions.ApiKey))
        {
            throw new InvalidOperationException(
                "hqqq-ingress: Tiingo:ApiKey is required. " +
                "Set Tiingo__ApiKey (or TIINGO_API_KEY legacy alias) to a real API key and restart.");
        }

        _logger.LogInformation(
            "TiingoIngressWorker: starting; basket topic={Topic} startupWait={Wait}s override={Override}",
            _basketOptions.Topic, _basketOptions.StartupWaitSeconds,
            _tiingoOptions.ResolveOverrideSymbols().Count);

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _state.SetRunning(true);
        try
        {
            var symbols = await ResolveInitialSymbolsAsync(stoppingToken).ConfigureAwait(false);
            if (symbols.Count == 0)
            {
                _logger.LogWarning(
                    "TiingoIngressWorker: exiting without subscribing — no basket arrived and no Tiingo:Symbols override configured");
                return;
            }

            _logger.LogInformation(
                "TiingoIngressWorker: subscribing to {Count} symbols (fingerprint={Fingerprint})",
                symbols.Count, _coordinator.AppliedFingerprint ?? "<bootstrap>");

            await RunSnapshotWarmupAsync(symbols, stoppingToken).ConfigureAwait(false);
            await RunWebsocketLoopAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _state.SetRunning(false);
            _logger.LogInformation("TiingoIngressWorker stopping");
        }
    }

    private async Task<IReadOnlyCollection<string>> ResolveInitialSymbolsAsync(CancellationToken ct)
    {
        // Fast path — a basket has already been consumed by the time the
        // worker starts iterating (depends on DI start order in the host).
        if (_universe.Current is { Symbols.Count: > 0 } seed)
        {
            await _coordinator.ApplyAsync(seed, ct).ConfigureAwait(false);
            return _coordinator.CurrentAppliedSymbols;
        }

        var waitSeconds = Math.Max(0, _basketOptions.StartupWaitSeconds);
        if (waitSeconds == 0)
        {
            _logger.LogWarning(
                "TiingoIngressWorker: StartupWaitSeconds=0; skipping basket wait and using override (if any)");
        }
        else
        {
            _logger.LogInformation(
                "TiingoIngressWorker: waiting up to {Wait}s for first basket on {Topic}",
                waitSeconds, _basketOptions.Topic);

            using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var arrived = new TaskCompletionSource<UniverseSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(UniverseSnapshot s)
            {
                if (s.Symbols.Count > 0) arrived.TrySetResult(s);
            }

            _universe.BasketUpdated += Handler;
            try
            {
                if (_universe.Current is { Symbols.Count: > 0 } immediate)
                    arrived.TrySetResult(immediate);

                var delay = Task.Delay(TimeSpan.FromSeconds(waitSeconds), waitCts.Token);
                var first = await Task.WhenAny(arrived.Task, delay).ConfigureAwait(false);

                if (first == arrived.Task)
                {
                    var snap = await arrived.Task.ConfigureAwait(false);
                    await _coordinator.ApplyAsync(snap, ct).ConfigureAwait(false);
                    return _coordinator.CurrentAppliedSymbols;
                }
            }
            finally
            {
                _universe.BasketUpdated -= Handler;
            }
        }

        var overrideSymbols = _tiingoOptions.ResolveOverrideSymbols();
        if (overrideSymbols.Count > 0)
        {
            _coordinator.SeedBootstrapSymbols(overrideSymbols);
            _logger.LogWarning(
                "TiingoIngressWorker: no basket within {Wait}s; falling back to Tiingo:Symbols override ({Count} symbols)",
                waitSeconds, overrideSymbols.Count);
            return _coordinator.CurrentAppliedSymbols;
        }

        return Array.Empty<string>();
    }

    private async Task RunSnapshotWarmupAsync(
        IReadOnlyCollection<string> symbols, CancellationToken ct)
    {
        if (!_tiingoOptions.SnapshotOnStartup) return;

        try
        {
            var snapshot = await _snapshotClient.FetchSnapshotsAsync(symbols, ct)
                .ConfigureAwait(false);

            if (snapshot.Count == 0)
            {
                _logger.LogInformation(
                    "Snapshot warmup returned no rows — relying on websocket for first ticks");
                return;
            }

            await _publisher.PublishBatchAsync(snapshot, ct).ConfigureAwait(false);
            _state.RecordPublishedTicks(snapshot.Count);
            _logger.LogInformation(
                "Snapshot warmup published {Count} baseline ticks", snapshot.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _state.RecordError($"snapshot warmup failed: {ex.Message}");
            _logger.LogWarning(ex,
                "Snapshot warmup failed — falling through to websocket loop");
        }
    }

    private async Task RunWebsocketLoopAsync(CancellationToken ct)
    {
        var attempt = 0;

        while (!ct.IsCancellationRequested)
        {
            var initial = _coordinator.CurrentAppliedSymbols;
            if (initial.Count == 0)
            {
                _logger.LogInformation(
                    "[ws-loop] no active symbols to subscribe; sleeping {Delay}s",
                    _tiingoOptions.ReconnectBaseDelaySeconds);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(
                        Math.Max(1, _tiingoOptions.ReconnectBaseDelaySeconds)), ct)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                continue;
            }

            try
            {
                attempt++;
                _logger.LogInformation(
                    "[ws-loop] attempt={Attempt} connecting to Tiingo ({Count} symbols)",
                    attempt, initial.Count);

                await _streamClient.ConnectAndStreamAsync(
                    initial,
                    PublishAndRecordAsync,
                    ct).ConfigureAwait(false);

                attempt = 0;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _state.RecordError($"ws-loop error: {ex.Message}");
                _logger.LogWarning(ex,
                    "[ws-loop] attempt={Attempt} failed; will reconnect with backoff", attempt);
            }

            if (ct.IsCancellationRequested) break;

            var delay = ComputeBackoff(attempt);
            _logger.LogInformation(
                "[ws-loop] sleeping {Delay}s before reconnect", delay.TotalSeconds);
            try
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Publish-and-record callback handed to <see cref="ITiingoStreamClient"/>.
    /// <see cref="IngestionState.RecordPublishedTick"/> is intentionally
    /// invoked only after the publisher's task completes successfully —
    /// failed publishes must not advance the runtime tick-flow signal that
    /// smoke proofs rely on.
    /// </summary>
    private async Task PublishAndRecordAsync(Hqqq.Contracts.Events.RawTickV1 tick, CancellationToken ct)
    {
        await _publisher.PublishAsync(tick, ct).ConfigureAwait(false);
        _state.RecordPublishedTick();
    }

    private TimeSpan ComputeBackoff(int attempt)
    {
        var baseSeconds = Math.Max(1, _tiingoOptions.ReconnectBaseDelaySeconds);
        var maxSeconds = Math.Max(baseSeconds, _tiingoOptions.MaxReconnectDelaySeconds);

        var seconds = baseSeconds * Math.Pow(2, Math.Max(0, attempt - 1));
        if (seconds > maxSeconds || double.IsInfinity(seconds)) seconds = maxSeconds;

        return TimeSpan.FromSeconds(seconds);
    }

    private static bool HasUsableApiKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var lowered = key.Trim().ToLowerInvariant();
        return !PlaceholderMarkers.Any(marker => lowered.Contains(marker));
    }
}
