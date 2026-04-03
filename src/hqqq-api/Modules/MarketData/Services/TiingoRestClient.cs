using System.Text.Json;
using Microsoft.Extensions.Options;
using Hqqq.Api.Configuration;
using Hqqq.Api.Modules.MarketData.Contracts;

namespace Hqqq.Api.Modules.MarketData.Services;

/// <summary>
/// Polls the Tiingo IEX REST endpoint as a fallback when the WebSocket is unavailable.
/// Uses <see cref="IHttpClientFactory"/> so the singleton is safe from captive-dependency issues.
/// </summary>
public sealed class TiingoRestClient
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly TiingoOptions _options;
    private readonly ILogger<TiingoRestClient> _logger;

    public TiingoRestClient(
        IHttpClientFactory httpFactory,
        IOptions<TiingoOptions> options,
        ILogger<TiingoRestClient> logger)
    {
        _httpFactory = httpFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Fetches latest IEX prices for the given symbols via Tiingo REST.
    /// Batches requests into groups of 50 symbols to stay within URL-length limits.
    /// </summary>
    public async Task<IReadOnlyList<PriceTick>> FetchLatestPricesAsync(
        IEnumerable<string> symbols, CancellationToken ct)
    {
        var allSymbols = symbols
            .Select(s => s.ToLowerInvariant())
            .Distinct()
            .ToList();

        if (allSymbols.Count == 0) return [];

        var results = new List<PriceTick>();

        foreach (var batch in allSymbols.Chunk(50))
        {
            var tickers = string.Join(",", batch);
            var url = $"{_options.RestBaseUrl}/?tickers={tickers}&token={_options.ApiKey}";

            try
            {
                using var http = _httpFactory.CreateClient();
                var response = await http.GetAsync(url, ct);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(ct);
                results.AddRange(ParseResponse(json));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Tiingo REST fetch failed for batch of {Count} symbols", batch.Length);
            }
        }

        return results;
    }

    // ── JSON parsing ────────────────────────────────────────────

    private IReadOnlyList<PriceTick> ParseResponse(string json)
    {
        var ticks = new List<PriceTick>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return ticks;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                try
                {
                    var tick = ParseItem(item);
                    if (tick is not null) ticks.Add(tick);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Skipping malformed Tiingo REST item");
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Tiingo REST response body");
        }
        return ticks;
    }

    private static PriceTick? ParseItem(JsonElement item)
    {
        var ticker = Str(item, "ticker");
        if (string.IsNullOrWhiteSpace(ticker)) return null;

        var price = Dec(item, "tngoLast")
                    ?? Dec(item, "last")
                    ?? Dec(item, "lastSalePrice")
                    ?? Dec(item, "mid");
        if (price is null or <= 0) return null;

        DateTimeOffset ts = DateTimeOffset.UtcNow;
        var tsStr = Str(item, "timestamp");
        if (tsStr is not null && DateTimeOffset.TryParse(tsStr, out var parsed))
            ts = parsed;

        return new PriceTick
        {
            Symbol = ticker.ToUpperInvariant(),
            Price = price.Value,
            Currency = "USD",
            Source = "rest",
            EventTimeUtc = ts,
            BidPrice = Dec(item, "bidPrice"),
            AskPrice = Dec(item, "askPrice"),
            PreviousClose = Dec(item, "prevClose"),
        };
    }

    private static string? Str(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static decimal? Dec(JsonElement el, string prop)
    {
        if (!el.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        try { return v.GetDecimal(); }
        catch { return null; }
    }
}
