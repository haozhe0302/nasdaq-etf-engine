using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Hqqq.Api.Configuration;
using Hqqq.Api.Modules.MarketData.Contracts;

namespace Hqqq.Api.Modules.MarketData.Services;

/// <summary>
/// Manages a single upstream Tiingo IEX WebSocket connection:
/// connect → subscribe → receive-loop → parse → update price store.
/// </summary>
public sealed class TiingoWebSocketClient : IDisposable
{
    private readonly TiingoOptions _options;
    private readonly ILatestPriceStore _priceStore;
    private readonly ILogger<TiingoWebSocketClient> _logger;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private ClientWebSocket? _ws;
    private volatile bool _connected;

    public bool IsConnected => _connected && _ws?.State == WebSocketState.Open;
    public DateTimeOffset LastHeartbeatUtc { get; private set; } = DateTimeOffset.MinValue;

    public TiingoWebSocketClient(
        IOptions<TiingoOptions> options,
        ILatestPriceStore priceStore,
        ILogger<TiingoWebSocketClient> logger)
    {
        _options = options.Value;
        _priceStore = priceStore;
        _logger = logger;
    }

    /// <summary>
    /// Connects to Tiingo, subscribes to the given symbols, and runs the
    /// receive loop until the connection drops or <paramref name="ct"/> is cancelled.
    /// </summary>
    public async Task ConnectAndRunAsync(IEnumerable<string> symbols, CancellationToken ct)
    {
        _ws?.Dispose();
        _ws = new ClientWebSocket();
        _connected = false;

        var uri = new Uri(_options.WebSocketUrl);
        _logger.LogInformation("Connecting to Tiingo IEX WebSocket at {Url}", uri);

        await _ws.ConnectAsync(uri, ct);
        _connected = true;
        _logger.LogInformation("Tiingo WebSocket connected");

        try
        {
            await SubscribeAsync(symbols, ct);
            await ReceiveLoopAsync(ct);
        }
        finally
        {
            _connected = false;
            await CloseGracefullyAsync();
        }
    }

    /// <summary>
    /// Sends a subscribe message for the given symbols over the open connection.
    /// Safe to call concurrently (serialized via semaphore).
    /// </summary>
    public async Task SubscribeAsync(IEnumerable<string> symbols, CancellationToken ct)
    {
        if (_ws?.State != WebSocketState.Open) return;

        var tickers = symbols.Select(s => s.ToLowerInvariant()).Distinct().ToList();
        var msg = JsonSerializer.Serialize(new
        {
            eventName = "subscribe",
            authorization = _options.ApiKey,
            eventData = new { thresholdLevel = 5, tickers }
        });

        _logger.LogInformation("Subscribing to {Count} symbols via WebSocket", tickers.Count);
        await SendAsync(msg, ct);
    }

    // ── Send / Receive ──────────────────────────────────────────

    private async Task SendAsync(string message, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct);
        try
        {
            if (_ws?.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(message);
            await _ws.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];
        var segment = new ArraySegment<byte>(buffer);
        using var messageBuffer = new MemoryStream();

        while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
        {
            messageBuffer.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await _ws.ReceiveAsync(segment, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogWarning("Tiingo WebSocket closed by server: {Status} {Desc}",
                        result.CloseStatus, result.CloseStatusDescription);
                    return;
                }
                messageBuffer.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            if (result.MessageType == WebSocketMessageType.Text)
            {
                var json = Encoding.UTF8.GetString(
                    messageBuffer.GetBuffer(), 0, (int)messageBuffer.Length);
                ProcessMessage(json);
            }
        }
    }

    // ── Message parsing ─────────────────────────────────────────

    private void ProcessMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var messageType = root.TryGetProperty("messageType", out var mt)
                ? mt.GetString() : null;

            switch (messageType)
            {
                case "H":
                    LastHeartbeatUtc = DateTimeOffset.UtcNow;
                    break;

                case "I":
                    LastHeartbeatUtc = DateTimeOffset.UtcNow;
                    if (_logger.IsEnabled(LogLevel.Debug))
                        _logger.LogDebug("Tiingo WS info: {Json}", json);
                    break;

                case "A":
                    LastHeartbeatUtc = DateTimeOffset.UtcNow;
                    ProcessDataMessage(root);
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Malformed Tiingo WS message, skipping");
        }
    }

    /// <summary>
    /// Parses a Tiingo IEX data array and updates the price store.
    /// Data layout: [updateType, date, timestamp, ticker, bidSize, bidPrice,
    ///               midPrice, askPrice, askSize, lastSalePrice, lastSize,
    ///               lastSaleTimestamp, ...]
    /// </summary>
    private void ProcessDataMessage(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return;
        if (data.GetArrayLength() < 10) return;

        var ticker = GetString(data, 3);
        if (string.IsNullOrWhiteSpace(ticker))
        {
            _logger.LogDebug("WS data with empty ticker, skipping");
            return;
        }

        var lastPrice = GetDecimal(data, 9) ?? GetDecimal(data, 6);
        if (lastPrice is null or <= 0)
        {
            _logger.LogDebug("WS: non-positive or missing price for {Ticker}, skipping", ticker);
            return;
        }

        DateTimeOffset eventTime = DateTimeOffset.UtcNow;
        var dateStr = GetString(data, 1);
        if (dateStr is not null && DateTimeOffset.TryParse(dateStr, out var parsed))
            eventTime = parsed;

        var bidPrice = GetDecimal(data, 5);
        var askPrice = GetDecimal(data, 7);

        _priceStore.Update(new PriceTick
        {
            Symbol = ticker.ToUpperInvariant(),
            Price = lastPrice.Value,
            Currency = "USD",
            Source = "ws",
            EventTimeUtc = eventTime,
            BidPrice = bidPrice > 0 ? bidPrice : null,
            AskPrice = askPrice > 0 ? askPrice : null,
        });
    }

    // ── JSON helpers ────────────────────────────────────────────

    private static string? GetString(JsonElement arr, int idx) =>
        idx < arr.GetArrayLength() && arr[idx].ValueKind == JsonValueKind.String
            ? arr[idx].GetString() : null;

    private static decimal? GetDecimal(JsonElement arr, int idx)
    {
        if (idx >= arr.GetArrayLength()) return null;
        var el = arr[idx];
        if (el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        try { return el.GetDecimal(); }
        catch { return null; }
    }

    // ── Lifecycle ───────────────────────────────────────────────

    private async Task CloseGracefullyAsync()
    {
        if (_ws is null) return;
        try
        {
            if (_ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutdown", cts.Token);
            }
        }
        catch { /* best-effort */ }
    }

    public void Dispose()
    {
        _ws?.Dispose();
        _sendLock.Dispose();
    }
}
