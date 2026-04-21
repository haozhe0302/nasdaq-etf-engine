using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hqqq.ReferenceData.Configuration;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Basket;

/// <summary>
/// Phase 2 port of <c>src/hqqq-api/Modules/Basket/Services/NasdaqHoldingsAdapter.cs</c>.
/// Fetches the Nasdaq-100 constituent list from the public Nasdaq
/// list-type endpoint. JSON response with 101 rows and approximate
/// market cap per ticker; provides weight via market-cap proxy and is
/// used both as a universe guardrail and as a secondary tail source
/// when AlphaVantage is absent or sparse.
/// </summary>
public sealed class NasdaqBasketAdapter : IBasketSourceAdapter<NasdaqBasketAdapter.RawResult>
{
    public const string AdapterName = "nasdaq";
    public const string HttpClientName = "hqqq-refdata-basket-nasdaq";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BasketOptions _basketOptions;
    private readonly ILogger<NasdaqBasketAdapter> _logger;

    public NasdaqBasketAdapter(
        IHttpClientFactory httpClientFactory,
        IOptions<ReferenceDataOptions> options,
        ILogger<NasdaqBasketAdapter> logger)
    {
        _httpClientFactory = httpClientFactory;
        _basketOptions = options.Value.Basket;
        _logger = logger;
    }

    public string Name => AdapterName;
    public bool Enabled => _basketOptions.Sources.Nasdaq.Enabled;

    public async Task<BasketSourceOutcome<RawResult>> FetchAsync(CancellationToken ct)
    {
        if (!Enabled)
            return BasketSourceOutcome<RawResult>.Disabled(AdapterName);

        var opts = _basketOptions.Sources.Nasdaq;

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, opts.TimeoutSeconds));

            using var req = new HttpRequestMessage(HttpMethod.Get, opts.Url);
            // Nasdaq's edge aggressively 403's default .NET UA; mirror
            // a realistic browser header set as Phase 1 did.
            req.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/120.0.0.0 Safari/537.36");
            req.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            req.Headers.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9");
            req.Headers.TryAddWithoutValidation("Referer", "https://www.nasdaq.com/");
            req.Headers.TryAddWithoutValidation("Origin", "https://www.nasdaq.com");

            using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return BasketSourceOutcome<RawResult>.Failed(
                    AdapterName, $"http status {(int)resp.StatusCode}");
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var payload = await JsonSerializer.DeserializeAsync<Wire>(stream, cancellationToken: ct)
                .ConfigureAwait(false);

            var rows = payload?.Data?.Data?.Rows;
            if (rows is null || rows.Count == 0)
                return BasketSourceOutcome<RawResult>.Failed(AdapterName, "empty rows");

            var parsed = new List<NasdaqEntry>(rows.Count);
            decimal totalCap = 0m;

            foreach (var r in rows)
            {
                var symbol = (r.Symbol ?? string.Empty).Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(symbol)) continue;

                var cap = ParseMarketCap(r.MarketCap);
                parsed.Add(new NasdaqEntry(symbol, (r.CompanyName ?? "Unknown").Trim(), cap));
                totalCap += cap;
            }

            var entries = totalCap > 0m
                ? parsed.Select(p => p with { Weight = p.MarketCap / totalCap * 100m }).ToList()
                : parsed;

            _logger.LogInformation(
                "NasdaqBasketAdapter: {Count} constituents, total market cap {Cap:N0}",
                entries.Count, totalCap);

            var result = new RawResult
            {
                Entries = entries,
                TotalMarketCap = totalCap,
            };

            return BasketSourceOutcome<RawResult>.Live(AdapterName, result, entries.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NasdaqBasketAdapter: fetch failed");
            return BasketSourceOutcome<RawResult>.Failed(AdapterName, ex.Message);
        }
    }

    private static decimal ParseMarketCap(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0m;
        var cleaned = raw.Replace("$", string.Empty).Replace(",", string.Empty).Trim();
        return decimal.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0m;
    }

    public sealed record RawResult
    {
        public required IReadOnlyList<NasdaqEntry> Entries { get; init; }
        public required decimal TotalMarketCap { get; init; }
    }

    public sealed record NasdaqEntry(string Symbol, string CompanyName, decimal MarketCap, decimal Weight = 0m);

    // ── Wire DTOs ──────────────────────────────────────────────

    internal sealed record Wire
    {
        [JsonPropertyName("data")] public WireOuter? Data { get; init; }
    }

    internal sealed record WireOuter
    {
        [JsonPropertyName("data")] public WireInner? Data { get; init; }
    }

    internal sealed record WireInner
    {
        [JsonPropertyName("rows")] public List<WireRow>? Rows { get; init; }
    }

    internal sealed record WireRow
    {
        [JsonPropertyName("symbol")] public string? Symbol { get; init; }
        [JsonPropertyName("companyName")] public string? CompanyName { get; init; }
        [JsonPropertyName("marketCap")] public string? MarketCap { get; init; }
    }
}
