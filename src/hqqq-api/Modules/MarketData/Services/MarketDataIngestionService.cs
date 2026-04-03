using Microsoft.Extensions.Options;
using Hqqq.Api.Configuration;
using Hqqq.Api.Modules.Basket.Contracts;
using Hqqq.Api.Modules.MarketData.Contracts;

namespace Hqqq.Api.Modules.MarketData.Services;

/// <summary>
/// Background service that orchestrates Tiingo WebSocket + REST fallback ingestion.
/// Watches the basket state and refreshes subscriptions when the symbol set changes.
/// Maintains exactly one upstream Tiingo WebSocket connection.
/// </summary>
public sealed class MarketDataIngestionService : BackgroundService, IMarketDataIngestionService
{
    private readonly IBasketSnapshotProvider _basketProvider;
    private readonly SubscriptionManager _subscriptions;
    private readonly ILatestPriceStore _priceStore;
    private readonly TiingoWebSocketClient _wsClient;
    private readonly TiingoRestClient _restClient;
    private readonly TiingoOptions _options;
    private readonly ILogger<MarketDataIngestionService> _logger;

    private CancellationTokenSource? _wsReconnectCts;
    private volatile bool _isRunning;
    private volatile bool _fallbackActive;
    private long _lastRestActivityTicks;
    private string _lastFingerprint = "";

    public bool IsRunning => _isRunning;
    public bool IsWebSocketConnected => _wsClient.IsConnected;
    public bool IsFallbackActive => _fallbackActive;

    public DateTimeOffset? LastActivityUtc
    {
        get
        {
            var ws = _wsClient.LastHeartbeatUtc;
            var restTicks = Interlocked.Read(ref _lastRestActivityTicks);
            var rest = restTicks > 0
                ? new DateTimeOffset(restTicks, TimeSpan.Zero)
                : DateTimeOffset.MinValue;
            var max = ws > rest ? ws : rest;
            return max > DateTimeOffset.MinValue ? max : null;
        }
    }

    public MarketDataIngestionService(
        IBasketSnapshotProvider basketProvider,
        SubscriptionManager subscriptions,
        ILatestPriceStore priceStore,
        TiingoWebSocketClient wsClient,
        TiingoRestClient restClient,
        IOptions<TiingoOptions> options,
        ILogger<MarketDataIngestionService> logger)
    {
        _basketProvider = basketProvider;
        _subscriptions = subscriptions;
        _priceStore = priceStore;
        _wsClient = wsClient;
        _restClient = restClient;
        _options = options.Value;
        _logger = logger;
    }

    // ── BackgroundService entry point ───────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || _options.ApiKey.Contains("YOUR_"))
        {
            _logger.LogWarning(
                "Tiingo API key not configured — MarketData ingestion is disabled. " +
                "Set TIINGO_API_KEY in .env to enable.");
            return;
        }

        _isRunning = true;
        _logger.LogInformation("MarketData ingestion service starting");

        await WaitForBasketAsync(stoppingToken);

        RefreshSubscriptionsFromBasket();
        _lastFingerprint = _subscriptions.GetFingerprint();

        try
        {
            await Task.WhenAll(
                RunWebSocketLoopAsync(stoppingToken),
                RunFallbackLoopAsync(stoppingToken),
                RunSubscriptionMonitorAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
        finally
        {
            _isRunning = false;
            _logger.LogInformation("MarketData ingestion service stopped");
        }
    }

    /// <summary>
    /// Polls the basket provider every 5 s for up to 60 s, waiting for at least
    /// an active basket to become available before starting ingestion.
    /// </summary>
    private async Task WaitForBasketAsync(CancellationToken ct)
    {
        for (int i = 0; i < 12; i++)
        {
            var state = _basketProvider.GetState();
            if (state.Active is not null)
            {
                _logger.LogInformation("Basket available — {Count} active symbols",
                    state.Active.Constituents.Count);
                return;
            }

            _logger.LogDebug("Waiting for basket to load ({Attempt}/12)", i + 1);
            await Task.Delay(5_000, ct);
        }

        _logger.LogWarning(
            "Basket not available after 60 s, starting with reference-only subscription (QQQ)");
    }

    // ── WebSocket loop ──────────────────────────────────────────

    private async Task RunWebSocketLoopAsync(CancellationToken ct)
    {
        int attempt = 0;

        while (!ct.IsCancellationRequested)
        {
            var symbols = _subscriptions.GetAllSymbols();
            if (symbols.Count == 0)
            {
                await Task.Delay(10_000, ct);
                continue;
            }

            _wsReconnectCts?.Dispose();
            _wsReconnectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var reconnectToken = _wsReconnectCts.Token;

            var hbBefore = _wsClient.LastHeartbeatUtc;

            try
            {
                await _wsClient.ConnectAndRunAsync(symbols, reconnectToken);

                // Only reset backoff if the session was productive (received data).
                // Outside market hours Tiingo closes the connection immediately
                // with NormalClosure — that must trigger backoff, not a tight loop.
                if (_wsClient.LastHeartbeatUtc > hbBefore)
                    attempt = 0;
                else
                    attempt++;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogInformation("WebSocket reconnecting due to subscription change");
                attempt = 0;
                continue;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                attempt++;
                _logger.LogError(ex, "WebSocket connection failed (attempt {Attempt})", attempt);
            }

            if (ct.IsCancellationRequested) return;

            if (attempt > 0)
            {
                var delaySec = Math.Min(
                    _options.ReconnectBaseDelaySeconds * Math.Pow(2, Math.Min(attempt - 1, 5)),
                    60);
                _logger.LogInformation("Reconnecting WebSocket in {Delay:F1}s", delaySec);

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySec), reconnectToken);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    _logger.LogInformation("Backoff interrupted by subscription change");
                    attempt = 0;
                }
            }
        }
    }

    // ── REST fallback loop ──────────────────────────────────────

    private async Task RunFallbackLoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(_options.RestPollingIntervalSeconds);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_wsClient.IsConnected)
                {
                    if (!_fallbackActive)
                    {
                        _logger.LogInformation(
                            "WebSocket not connected, activating REST fallback (every {Sec}s)",
                            _options.RestPollingIntervalSeconds);
                        _fallbackActive = true;
                    }

                    var symbols = _subscriptions.GetAllSymbols();
                    if (symbols.Count > 0)
                    {
                        _logger.LogDebug("REST fallback polling {Count} symbols", symbols.Count);
                        var ticks = await _restClient.FetchLatestPricesAsync(symbols, ct);

                        foreach (var tick in ticks)
                            _priceStore.Update(tick);

                        if (ticks.Count > 0)
                            Interlocked.Exchange(ref _lastRestActivityTicks,
                                DateTimeOffset.UtcNow.UtcTicks);
                    }
                }
                else if (_fallbackActive)
                {
                    _logger.LogInformation("WebSocket recovered, deactivating REST fallback");
                    _fallbackActive = false;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "REST fallback polling error");
            }

            await Task.Delay(interval, ct);
        }
    }

    // ── Subscription monitor ────────────────────────────────────

    private async Task RunSubscriptionMonitorAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);

            try
            {
                var oldFp = _lastFingerprint;
                RefreshSubscriptionsFromBasket();
                var newFp = _subscriptions.GetFingerprint();

                if (newFp != oldFp)
                {
                    _lastFingerprint = newFp;
                    var count = _subscriptions.GetAllSymbols().Count;
                    _logger.LogInformation(
                        "Subscription set changed ({Count} symbols), triggering WS reconnect",
                        count);

                    try { _wsReconnectCts?.Cancel(); }
                    catch (ObjectDisposedException) { /* race with WS loop dispose */ }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Subscription monitor error");
            }
        }
    }

    // ── Basket integration ──────────────────────────────────────

    private void RefreshSubscriptionsFromBasket()
    {
        var state = _basketProvider.GetState();
        var activeSymbols = state.Active?.Constituents.Select(c => c.Symbol);
        var pendingSymbols = state.Pending?.Constituents.Select(c => c.Symbol);

        _subscriptions.UpdateFromBasketState(activeSymbols, pendingSymbols);
        _priceStore.SetTrackedSymbols(_subscriptions.GetSymbolRoles());
    }
}
