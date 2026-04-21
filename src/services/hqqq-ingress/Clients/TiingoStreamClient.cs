using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using Hqqq.Contracts.Events;
using Hqqq.Ingress.Configuration;
using Hqqq.Ingress.Normalization;
using Hqqq.Ingress.State;
using Microsoft.Extensions.Options;

namespace Hqqq.Ingress.Clients;

/// <summary>
/// Real Tiingo IEX websocket client. Single runtime path — there is no
/// hybrid/stub implementation anymore.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the legacy monolith's <c>TiingoWebSocketClient</c>: lowercases
/// tickers in the subscribe payload, recognises <c>A</c>/<c>H</c>/<c>I</c>/<c>E</c>
/// frames, supports the compact 3-element data format observed on the
/// IEX feed, and updates <see cref="IngestionState"/> on connect / data /
/// error. The caller (the worker) owns the reconnect/backoff loop — this
/// client never retries internally.
/// </para>
/// <para>
/// Supports mid-session subscribe/unsubscribe so the ingress service can
/// follow basket activation events without dropping the socket. When the
/// socket is not open, pending mutations are queued in
/// <see cref="_desiredSymbols"/> and re-applied on the next connect.
/// </para>
/// </remarks>
public sealed class TiingoStreamClient : ITiingoStreamClient, IDisposable
{
    private readonly TiingoOptions _options;
    private readonly IngestionState _state;
    private readonly ILogger<TiingoStreamClient> _logger;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly object _symbolLock = new();
    private readonly HashSet<string> _desiredSymbols = new(StringComparer.Ordinal);

    private ClientWebSocket? _ws;
    private long _sequence;

    public TiingoStreamClient(
        IOptions<TiingoOptions> options,
        IngestionState state,
        ILogger<TiingoStreamClient> logger)
    {
        _options = options.Value;
        _state = state;
        _logger = logger;
    }

    public bool IsConnected => _ws?.State == WebSocketState.Open;
    public DateTimeOffset? LastDataUtc => _state.LastActivityUtc;

    public async Task ConnectAndStreamAsync(
        IEnumerable<string> symbols,
        Func<RawTickV1, CancellationToken, Task> onTick,
        CancellationToken ct)
    {
        _ws?.Dispose();
        _ws = new ClientWebSocket();

        lock (_symbolLock)
        {
            _desiredSymbols.Clear();
            foreach (var s in symbols ?? Enumerable.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(s))
                    _desiredSymbols.Add(s.ToLowerInvariant());
            }
        }

        var uri = new Uri(_options.WsUrl);
        _logger.LogInformation("[WS:connect-start] Connecting to Tiingo IEX at {Url}", uri);

        try
        {
            await _ws.ConnectAsync(uri, ct).ConfigureAwait(false);
            _state.SetWebSocketConnected(true);
            _logger.LogInformation("[WS:connect-ok] Tiingo websocket connected");

            string[] initial;
            lock (_symbolLock) initial = _desiredSymbols.ToArray();

            await SendSubscribeAsync(initial, ct).ConfigureAwait(false);
            await ReceiveLoopAsync(onTick, ct).ConfigureAwait(false);
        }
        finally
        {
            _state.SetWebSocketConnected(false);
            await CloseGracefullyAsync().ConfigureAwait(false);
        }
    }

    public async Task SubscribeAsync(IEnumerable<string> symbols, CancellationToken ct)
    {
        var toAdd = NormalizeAndTrack(symbols, add: true);
        if (toAdd.Length == 0) return;

        if (_ws?.State != WebSocketState.Open)
        {
            _logger.LogDebug(
                "[WS:subscribe-deferred] {Count} symbols will be added on next connect",
                toAdd.Length);
            return;
        }

        await SendSubscribeAsync(toAdd, ct).ConfigureAwait(false);
    }

    public async Task UnsubscribeAsync(IEnumerable<string> symbols, CancellationToken ct)
    {
        var toRemove = NormalizeAndTrack(symbols, add: false);
        if (toRemove.Length == 0) return;

        if (_ws?.State != WebSocketState.Open)
        {
            _logger.LogDebug(
                "[WS:unsubscribe-deferred] {Count} symbols will be dropped on next connect",
                toRemove.Length);
            return;
        }

        await SendUnsubscribeAsync(toRemove, ct).ConfigureAwait(false);
    }

    private string[] NormalizeAndTrack(IEnumerable<string> symbols, bool add)
    {
        if (symbols is null) return Array.Empty<string>();

        var changes = new List<string>();
        lock (_symbolLock)
        {
            foreach (var raw in symbols)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var lower = raw.Trim().ToLowerInvariant();
                if (add)
                {
                    if (_desiredSymbols.Add(lower)) changes.Add(lower);
                }
                else
                {
                    if (_desiredSymbols.Remove(lower)) changes.Add(lower);
                }
            }
        }
        return changes.ToArray();
    }

    private async Task SendSubscribeAsync(IReadOnlyCollection<string> tickers, CancellationToken ct)
    {
        if (_ws?.State != WebSocketState.Open || tickers.Count == 0) return;

        var msg = JsonSerializer.Serialize(new
        {
            eventName = "subscribe",
            authorization = _options.ApiKey,
            eventData = new
            {
                thresholdLevel = _options.WebSocketThresholdLevel,
                tickers,
            },
        });

        _logger.LogInformation(
            "[WS:subscribe-send] Subscribing to {Count} tickers (thresholdLevel={Level})",
            tickers.Count, _options.WebSocketThresholdLevel);

        await SendAsync(msg, ct).ConfigureAwait(false);
    }

    private async Task SendUnsubscribeAsync(IReadOnlyCollection<string> tickers, CancellationToken ct)
    {
        if (_ws?.State != WebSocketState.Open || tickers.Count == 0) return;

        var msg = JsonSerializer.Serialize(new
        {
            eventName = "unsubscribe",
            authorization = _options.ApiKey,
            eventData = new
            {
                thresholdLevel = _options.WebSocketThresholdLevel,
                tickers,
            },
        });

        _logger.LogInformation("[WS:unsubscribe-send] Unsubscribing {Count} tickers", tickers.Count);

        await SendAsync(msg, ct).ConfigureAwait(false);
    }

    private async Task SendAsync(string message, CancellationToken ct)
    {
        await _sendLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_ws?.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(message);
            await _ws.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, ct)
                .ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(
        Func<RawTickV1, CancellationToken, Task> onTick,
        CancellationToken ct)
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
                result = await _ws.ReceiveAsync(segment, ct).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogWarning(
                        "[WS:close-by-server] Tiingo closed: {Status} {Desc}",
                        result.CloseStatus, result.CloseStatusDescription);
                    return;
                }

                messageBuffer.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text) continue;

            var json = Encoding.UTF8.GetString(
                messageBuffer.GetBuffer(), 0, (int)messageBuffer.Length);

            await ProcessMessageAsync(json, onTick, ct).ConfigureAwait(false);
        }
    }

    private async Task ProcessMessageAsync(
        string json,
        Func<RawTickV1, CancellationToken, Task> onTick,
        CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var messageType = root.TryGetProperty("messageType", out var mt)
                ? mt.GetString()
                : null;

            switch (messageType)
            {
                case "H":
                    return;

                case "I":
                    _logger.LogInformation("Tiingo info frame: {Json}", json);
                    return;

                case "E":
                    HandleErrorFrame(root, json);
                    return;

                case "A":
                    var tick = TryParseDataFrame(root);
                    if (tick is not null)
                    {
                        _state.RecordTick();
                        await onTick(tick, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        _logger.LogDebug("Skipping unrecognised A-frame: {Json}", json);
                    }
                    return;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Malformed Tiingo WS message; skipping");
        }
    }

    private void HandleErrorFrame(JsonElement root, string raw)
    {
        string? message = null;
        int? code = null;

        if (root.TryGetProperty("response", out var resp))
        {
            if (resp.TryGetProperty("code", out var codeProp)
                && codeProp.ValueKind == JsonValueKind.Number)
            {
                code = codeProp.GetInt32();
            }

            if (resp.TryGetProperty("message", out var msgProp)
                && msgProp.ValueKind == JsonValueKind.String)
            {
                message = msgProp.GetString();
            }
        }

        message ??= raw;
        _state.RecordError($"upstream-error code={code}: {message}");
        _logger.LogError(
            "[WS:upstream-error] code={Code} message={Message}",
            code, message);
    }

    /// <summary>
    /// Parses an <c>A</c> data frame into a <see cref="RawTickV1"/>.
    /// Supports both the compact 3-element form (<c>[ts, ticker, price]</c>)
    /// observed on the live feed and the legacy verbose 12-element form.
    /// </summary>
    internal RawTickV1? TryParseDataFrame(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return null;

        var len = data.GetArrayLength();

        if (len >= 3 && len < 10)
        {
            var ts = ReadString(data, 0);
            var ticker = ReadString(data, 1);
            var price = ReadDecimal(data, 2);

            if (string.IsNullOrWhiteSpace(ticker) || price is null or <= 0m)
                return null;

            var providerTime = ParseTimestamp(ts);
            return BuildTick(ticker!, price.Value, bid: null, ask: null, providerTime);
        }

        if (len >= 10)
        {
            var ticker = ReadString(data, 3);
            if (string.IsNullOrWhiteSpace(ticker)) return null;

            var lastPrice = ReadDecimal(data, 9) ?? ReadDecimal(data, 6);
            if (lastPrice is null or <= 0m) return null;

            var providerTime = ParseTimestamp(ReadString(data, 1));
            var bid = ReadDecimal(data, 5);
            var ask = ReadDecimal(data, 7);

            return BuildTick(
                ticker!, lastPrice.Value,
                bid is > 0m ? bid : null,
                ask is > 0m ? ask : null,
                providerTime);
        }

        return null;
    }

    private RawTickV1 BuildTick(
        string ticker, decimal last, decimal? bid, decimal? ask, DateTimeOffset providerTime)
    {
        return TiingoQuoteNormalizer.Normalize(
            symbol: ticker.ToUpperInvariant(),
            last: last,
            bid: bid,
            ask: ask,
            currency: "USD",
            providerTimestamp: providerTime,
            sequence: Interlocked.Increment(ref _sequence));
    }

    private static DateTimeOffset ParseTimestamp(string? raw)
    {
        if (!string.IsNullOrWhiteSpace(raw)
            && DateTimeOffset.TryParse(raw, out var parsed))
        {
            return parsed.ToUniversalTime();
        }
        return DateTimeOffset.UtcNow;
    }

    private static string? ReadString(JsonElement arr, int idx) =>
        idx < arr.GetArrayLength() && arr[idx].ValueKind == JsonValueKind.String
            ? arr[idx].GetString()
            : null;

    private static decimal? ReadDecimal(JsonElement arr, int idx)
    {
        if (idx >= arr.GetArrayLength()) return null;
        var el = arr[idx];
        if (el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        try { return el.GetDecimal(); }
        catch { return null; }
    }

    private async Task CloseGracefullyAsync()
    {
        if (_ws is null) return;
        try
        {
            if (_ws.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", cts.Token)
                    .ConfigureAwait(false);
            }
        }
        catch
        {
            // Best-effort close; the socket is being torn down anyway.
        }
    }

    public void Dispose()
    {
        _ws?.Dispose();
        _sendLock.Dispose();
    }
}
