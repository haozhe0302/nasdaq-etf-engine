using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hqqq.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Hqqq.Api.Modules.Basket.Services;

/// <summary>
/// Fetches QQQ full holdings from the Alpha Vantage ETF_PROFILE endpoint.
///
/// Source characteristics:
///   • JSON response with ~103 holdings, sector breakdown, and net assets.
///   • Provides weight per holding but no shares held or snapshot date.
///   • Contains non-equity rows (futures, cash) with symbol = "n/a" that
///     must be filtered before use.
///   • Used as the preferred tail source after anchor holdings are locked.
/// </summary>
public sealed class AlphaVantageAdapter
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AlphaVantageAdapter> _logger;
    private readonly BasketOptions _options;

    public AlphaVantageAdapter(
        HttpClient httpClient,
        IOptions<BasketOptions> options,
        ILogger<AlphaVantageAdapter> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
    }

    public sealed record AlphaVantageResult
    {
        public required List<AlphaHolding> Holdings { get; init; }
        public required List<AlphaSector> Sectors { get; init; }
        public required decimal NetAssets { get; init; }
        public required int RawCount { get; init; }
        public required int FilteredCount { get; init; }
    }

    public sealed record AlphaHolding(
        string Symbol, string Description, decimal Weight);

    public sealed record AlphaSector(string Sector, decimal Weight);

    public async Task<AlphaVantageResult> FetchAsync(CancellationToken ct = default)
    {
        var apiKey = _options.AlphaVantageApiKey;
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Contains("YOUR_"))
        {
            throw new InvalidOperationException(
                "Alpha Vantage API key not configured. Set ALPHA_VANTAGE_API_KEY in .env");
        }

        var url = $"{_options.AlphaVantageBaseUrl}?function=ETF_PROFILE&symbol=QQQ&apikey={apiKey}";
        _logger.LogInformation("Fetching QQQ holdings from Alpha Vantage");

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add("User-Agent", "hqqq-engine/1.0");

        using var resp = await _httpClient.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        var payload = await JsonSerializer.DeserializeAsync<AvResponse>(stream, cancellationToken: ct);

        if (payload?.Holdings is null || payload.Holdings.Count == 0)
            throw new InvalidOperationException("Alpha Vantage returned empty holdings");

        var rawCount = payload.Holdings.Count;

        var filtered = payload.Holdings
            .Where(IsValidEquityRow)
            .Select(h => new AlphaHolding(
                h.Symbol?.Trim().ToUpperInvariant() ?? "",
                h.Description?.Trim() ?? "Unknown",
                ParseWeight(h.Weight)))
            .Where(h => !string.IsNullOrWhiteSpace(h.Symbol))
            .ToList();

        var sectors = (payload.Sectors ?? [])
            .Select(s => new AlphaSector(
                s.Sector?.Trim() ?? "Unknown",
                ParseWeight(s.Weight)))
            .ToList();

        decimal.TryParse(payload.NetAssets, CultureInfo.InvariantCulture, out var netAssets);

        _logger.LogInformation(
            "AlphaVantage: {Raw} raw → {Filtered} filtered holdings, {Sectors} sectors",
            rawCount, filtered.Count, sectors.Count);

        return new AlphaVantageResult
        {
            Holdings = filtered,
            Sectors = sectors,
            NetAssets = netAssets,
            RawCount = rawCount,
            FilteredCount = filtered.Count,
        };
    }

    private static bool IsValidEquityRow(AvHolding h)
    {
        if (string.IsNullOrWhiteSpace(h.Symbol) || h.Symbol.Equals("n/a", StringComparison.OrdinalIgnoreCase))
            return false;

        var desc = h.Description?.ToUpperInvariant() ?? "";
        if (desc.Contains("CASH") || desc.Contains("FUTURE") || desc.Contains("MONEY MARKET"))
            return false;

        return true;
    }

    private static decimal ParseWeight(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0m;
        return decimal.TryParse(raw, CultureInfo.InvariantCulture, out var v) ? v : 0m;
    }

    #region Alpha Vantage DTOs

    internal sealed record AvResponse
    {
        [JsonPropertyName("net_assets")]
        public string? NetAssets { get; init; }

        [JsonPropertyName("holdings")]
        public List<AvHolding>? Holdings { get; init; }

        [JsonPropertyName("sectors")]
        public List<AvSectorDto>? Sectors { get; init; }
    }

    internal sealed record AvHolding
    {
        [JsonPropertyName("symbol")]
        public string? Symbol { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("weight")]
        public string? Weight { get; init; }
    }

    internal sealed record AvSectorDto
    {
        [JsonPropertyName("sector")]
        public string? Sector { get; init; }

        [JsonPropertyName("weight")]
        public string? Weight { get; init; }
    }

    #endregion
}
