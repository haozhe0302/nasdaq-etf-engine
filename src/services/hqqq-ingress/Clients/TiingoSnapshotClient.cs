using System.Text.Json;
using System.Threading;
using Hqqq.Contracts.Events;
using Hqqq.Ingress.Configuration;
using Hqqq.Ingress.Normalization;
using Microsoft.Extensions.Options;

namespace Hqqq.Ingress.Clients;

/// <summary>
/// Real Tiingo IEX REST snapshot client. Used at standalone startup to
/// publish a one-shot baseline tick per subscribed symbol so consumers
/// have data before the first websocket tick.
/// </summary>
/// <remarks>
/// Mirrors the legacy monolith's <c>TiingoRestClient</c> shape: batches
/// requests into chunks of 50 symbols to stay under URL length limits,
/// reads <c>tngoLast</c>/<c>last</c>/<c>lastSalePrice</c>/<c>mid</c>
/// fields in priority order, and skips items whose price is missing or
/// non-positive.
/// </remarks>
public sealed class TiingoSnapshotClient : ITiingoSnapshotClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly TiingoOptions _options;
    private readonly ILogger<TiingoSnapshotClient> _logger;
    private long _sequence;

    public TiingoSnapshotClient(
        IHttpClientFactory httpFactory,
        IOptions<TiingoOptions> options,
        ILogger<TiingoSnapshotClient> logger)
    {
        _httpFactory = httpFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RawTickV1>> FetchSnapshotsAsync(
        IEnumerable<string> symbols, CancellationToken ct)
    {
        var allSymbols = symbols
            .Select(s => s.ToLowerInvariant())
            .Distinct()
            .ToArray();

        if (allSymbols.Length == 0) return Array.Empty<RawTickV1>();

        var ticks = new List<RawTickV1>();

        foreach (var batch in allSymbols.Chunk(50))
        {
            var tickers = string.Join(",", batch);
            var url = $"{_options.RestBaseUrl}/?tickers={tickers}&token={_options.ApiKey}";

            try
            {
                using var http = _httpFactory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(10);

                var response = await http.GetAsync(url, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                ticks.AddRange(ParseResponse(json));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Tiingo REST snapshot failed for batch of {Count} symbols", batch.Length);
            }
        }

        return ticks;
    }

    private IEnumerable<RawTickV1> ParseResponse(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Tiingo REST response body");
            yield break;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array) yield break;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var ticker = ReadString(item, "ticker");
                if (string.IsNullOrWhiteSpace(ticker)) continue;

                var price = ReadDecimal(item, "tngoLast")
                            ?? ReadDecimal(item, "last")
                            ?? ReadDecimal(item, "lastSalePrice")
                            ?? ReadDecimal(item, "mid");
                if (price is null or <= 0m) continue;

                var ts = DateTimeOffset.UtcNow;
                var tsRaw = ReadString(item, "timestamp");
                if (tsRaw is not null && DateTimeOffset.TryParse(tsRaw, out var parsed))
                    ts = parsed.ToUniversalTime();

                yield return TiingoQuoteNormalizer.Normalize(
                    symbol: ticker.ToUpperInvariant(),
                    last: price.Value,
                    bid: ReadDecimal(item, "bidPrice"),
                    ask: ReadDecimal(item, "askPrice"),
                    currency: "USD",
                    providerTimestamp: ts,
                    sequence: Interlocked.Increment(ref _sequence));
            }
        }
    }

    private static string? ReadString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static decimal? ReadDecimal(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        try { return v.GetDecimal(); }
        catch { return null; }
    }
}
