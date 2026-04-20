using Hqqq.Infrastructure.Hosting;
using Hqqq.Ingress.Clients;
using Hqqq.Ingress.Configuration;
using Hqqq.Ingress.Publishing;
using Hqqq.Ingress.State;
using Microsoft.Extensions.Options;

namespace Hqqq.Ingress.Workers;

/// <summary>
/// Hosted worker that orchestrates Tiingo ingestion. Behaviour is
/// gated by <see cref="OperatingMode"/>:
/// <list type="bullet">
///   <item>
///     <see cref="OperatingMode.Hybrid"/> — idle. The legacy monolith
///     bridges ticks. If a Tiingo API key is present we log a single
///     warning so operators don't think their key is "active" when it
///     is intentionally ignored.
///   </item>
///   <item>
///     <see cref="OperatingMode.Standalone"/> — validate config (fail
///     fast if the API key is missing/placeholder), run a one-shot REST
///     snapshot warmup so consumers see a baseline price, then loop the
///     websocket with bounded exponential backoff.
///   </item>
/// </list>
/// </summary>
public sealed class TiingoIngressWorker : BackgroundService
{
    private static readonly string[] PlaceholderMarkers = new[] { "<set", "your_", "changeme" };

    private readonly ITiingoStreamClient _streamClient;
    private readonly ITiingoSnapshotClient _snapshotClient;
    private readonly ITickPublisher _publisher;
    private readonly IngestionState _state;
    private readonly TiingoOptions _options;
    private readonly OperatingModeOptions _mode;
    private readonly ILogger<TiingoIngressWorker> _logger;

    public TiingoIngressWorker(
        ITiingoStreamClient streamClient,
        ITiingoSnapshotClient snapshotClient,
        ITickPublisher publisher,
        IngestionState state,
        IOptions<TiingoOptions> options,
        OperatingModeOptions mode,
        ILogger<TiingoIngressWorker> logger)
    {
        _streamClient = streamClient;
        _snapshotClient = snapshotClient;
        _publisher = publisher;
        _state = state;
        _options = options.Value;
        _mode = mode;
        _logger = logger;
    }

    /// <summary>
    /// Validates standalone preconditions before the host advertises as
    /// started. Throws so the process exits and the orchestrator (Container
    /// App / docker-compose) restarts with the operator-visible error.
    /// </summary>
    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (_mode.IsStandalone)
        {
            if (!HasUsableApiKey(_options.ApiKey))
            {
                throw new InvalidOperationException(
                    "hqqq-ingress: HQQQ_OPERATING_MODE=standalone requires Tiingo:ApiKey " +
                    "to be set to a real API key. Set Tiingo__ApiKey or TIINGO_API_KEY and restart.");
            }

            _logger.LogInformation(
                "TiingoIngressWorker: standalone mode — Tiingo ingestion will start");
        }
        else
        {
            if (HasUsableApiKey(_options.ApiKey))
            {
                _logger.LogWarning(
                    "TiingoIngressWorker: hybrid mode — Tiingo:ApiKey is present but ignored. " +
                    "Set HQQQ_OPERATING_MODE=standalone to activate Phase 2 native ingestion.");
            }
            else
            {
                _logger.LogInformation(
                    "TiingoIngressWorker: hybrid mode — ingestion is delegated to the legacy monolith");
            }
        }

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _state.SetRunning(true);
        try
        {
            if (_mode.IsHybrid)
            {
                // Stream client in hybrid is the no-op stub; awaiting it
                // simply blocks until cancellation, which keeps the
                // /healthz/* probes serving.
                await _streamClient.ConnectAndStreamAsync(
                    Array.Empty<string>(),
                    (_, _) => Task.CompletedTask,
                    stoppingToken).ConfigureAwait(false);
                return;
            }

            var symbols = _options.ResolveSymbols();
            _logger.LogInformation(
                "Standalone ingest will subscribe to {Count} symbols", symbols.Count);

            await RunSnapshotWarmupAsync(symbols, stoppingToken).ConfigureAwait(false);
            await RunWebsocketLoopAsync(symbols, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            _state.SetRunning(false);
            _logger.LogInformation("TiingoIngressWorker stopping");
        }
    }

    private async Task RunSnapshotWarmupAsync(
        IReadOnlyList<string> symbols, CancellationToken ct)
    {
        if (!_options.SnapshotOnStartup) return;

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

    private async Task RunWebsocketLoopAsync(
        IReadOnlyList<string> symbols, CancellationToken ct)
    {
        var attempt = 0;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                attempt++;
                _logger.LogInformation(
                    "[ws-loop] attempt={Attempt} connecting to Tiingo", attempt);

                await _streamClient.ConnectAndStreamAsync(
                    symbols,
                    (tick, innerCt) => _publisher.PublishAsync(tick, innerCt),
                    ct).ConfigureAwait(false);

                // Clean exit (server-initiated close). Reset attempt
                // counter so the next reconnect uses the base delay.
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

    private TimeSpan ComputeBackoff(int attempt)
    {
        var baseSeconds = Math.Max(1, _options.ReconnectBaseDelaySeconds);
        var maxSeconds = Math.Max(baseSeconds, _options.MaxReconnectDelaySeconds);

        // Exponential: base * 2^(attempt-1), capped.
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
