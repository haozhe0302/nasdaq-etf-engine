using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hqqq.Api.Configuration;
using Hqqq.Api.Modules.Basket.Contracts;
using Microsoft.Extensions.Options;

namespace Hqqq.Api.Modules.Basket.Services;

/// <summary>
/// Fetches Nasdaq-100 index constituents from the official Nasdaq API.
///
/// Source selection rationale:
///   The preferred source would be the Invesco QQQ holdings CSV download at
///   invesco.com, but that endpoint serves an SPA HTML page requiring JavaScript
///   execution and cannot be consumed with a simple HTTP GET (confirmed Apr 2026).
///   Browser-automation tools (Puppeteer, Playwright, Selenium) are out of scope.
///
///   The Nasdaq.com public API at /api/quote/list-type/nasdaq100 returns a
///   structured JSON response with all current Nasdaq-100 constituents, company
///   names, and market-cap figures. Because QQQ tracks the Nasdaq-100 Index,
///   this is the closest official machine-readable source that is programmatically
///   accessible without authentication or browser automation.
///
///   Weights are computed from market-cap proportions, which is the correct
///   methodology for a modified market-cap-weighted index. Shares held in the
///   ETF basket are not available from this endpoint and are set to zero.
/// </summary>
public sealed class NasdaqHoldingsAdapter
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<NasdaqHoldingsAdapter> _logger;
    private readonly BasketOptions _options;

    public NasdaqHoldingsAdapter(
        HttpClient httpClient,
        IOptions<BasketOptions> options,
        ILogger<NasdaqHoldingsAdapter> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<BasketConstituent>> FetchAsync(CancellationToken ct = default)
    {
        var url = _options.HoldingsSourceUrl;
        _logger.LogInformation("Fetching Nasdaq-100 constituents from {Url}", url);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "hqqq-engine/1.0");
        request.Headers.Add("Accept", "application/json");

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var payload = await JsonSerializer.DeserializeAsync<NasdaqApiResponse>(stream, cancellationToken: ct);

        if (payload?.Status?.RCode != 200 || payload.Data?.Inner?.Rows is not { Count: > 0 } rows)
            throw new InvalidOperationException("Nasdaq API returned an empty or invalid response");

        var asOfDate = ParseAsOfDate(payload.Data.Date);

        var totalMarketCap = rows.Sum(r => ParseMarketCap(r.MarketCap));
        if (totalMarketCap <= 0)
            throw new InvalidOperationException("Total market cap is zero; cannot compute weights");

        var constituents = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Symbol))
            .Select(r =>
            {
                var mktCap = ParseMarketCap(r.MarketCap);
                return new BasketConstituent
                {
                    Symbol = r.Symbol.Trim().ToUpperInvariant(),
                    SecurityName = CleanCompanyName(r.CompanyName),
                    Exchange = "NASDAQ",
                    Currency = "USD",
                    SharesHeld = 0m,
                    Weight = totalMarketCap > 0 ? Math.Round(mktCap / totalMarketCap, 8) : null,
                    Sector = string.IsNullOrWhiteSpace(r.Sector) ? "Unknown" : r.Sector.Trim(),
                    AsOfDate = asOfDate,
                };
            })
            .ToList();

        _logger.LogInformation(
            "Parsed {Count} constituents as of {AsOf}", constituents.Count, asOfDate);

        return constituents;
    }

    private static DateOnly ParseAsOfDate(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr))
            return DateOnly.FromDateTime(DateTime.UtcNow);

        if (DateOnly.TryParseExact(dateStr, "MMM d, yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;

        if (DateOnly.TryParse(dateStr, CultureInfo.InvariantCulture, out d))
            return d;

        return DateOnly.FromDateTime(DateTime.UtcNow);
    }

    private static decimal ParseMarketCap(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return 0m;

        var cleaned = raw.Replace(",", "");
        return decimal.TryParse(cleaned, CultureInfo.InvariantCulture, out var v) ? v : 0m;
    }

    private static string CleanCompanyName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "Unknown";

        ReadOnlySpan<string> suffixes =
        [
            " Common Stock", " Class A Common Stock", " Class B Common Stock",
            " Class C Capital Stock", " Class A Subordinate Voting Shares",
            " Ordinary Shares", " American Depositary Shares",
            " Common Shares", " New York Registry Shares",
        ];

        var name = raw.Trim();
        foreach (var suffix in suffixes)
        {
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[..^suffix.Length].TrimEnd();
                break;
            }
        }

        return name;
    }

    #region Nasdaq API DTOs (internal)

    internal sealed record NasdaqApiResponse
    {
        [JsonPropertyName("data")]
        public NasdaqDataEnvelope? Data { get; init; }

        [JsonPropertyName("status")]
        public NasdaqStatusBlock? Status { get; init; }
    }

    internal sealed record NasdaqDataEnvelope
    {
        [JsonPropertyName("totalrecords")]
        public int TotalRecords { get; init; }

        [JsonPropertyName("date")]
        public string? Date { get; init; }

        [JsonPropertyName("data")]
        public NasdaqDataInner? Inner { get; init; }
    }

    internal sealed record NasdaqDataInner
    {
        [JsonPropertyName("rows")]
        public List<NasdaqRow>? Rows { get; init; }
    }

    internal sealed record NasdaqRow
    {
        [JsonPropertyName("symbol")]
        public string Symbol { get; init; } = "";

        [JsonPropertyName("companyName")]
        public string CompanyName { get; init; } = "";

        [JsonPropertyName("marketCap")]
        public string MarketCap { get; init; } = "";

        [JsonPropertyName("sector")]
        public string Sector { get; init; } = "";
    }

    internal sealed record NasdaqStatusBlock
    {
        [JsonPropertyName("rCode")]
        public int RCode { get; init; }
    }

    #endregion
}
