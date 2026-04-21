using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hqqq.ReferenceData.Configuration;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Basket;

/// <summary>
/// Phase 2 port of <c>src/hqqq-api/Modules/Basket/Services/AlphaVantageAdapter.cs</c>.
/// Fetches QQQ full holdings from the Alpha Vantage
/// <c>ETF_PROFILE</c> endpoint. JSON response with ~103 holdings, sector
/// breakdown, and net assets; provides weight per holding but no shares
/// held or snapshot date.
/// </summary>
/// <remarks>
/// Used as the preferred tail source after anchor holdings are locked.
/// Non-equity rows (futures, cash, <c>symbol == "n/a"</c>) are filtered
/// out before projection. The adapter never throws — all failures are
/// surfaced as <see cref="BasketSourceOutcome{T}"/> with
/// <c>Success=false</c>.
/// </remarks>
public sealed class AlphaVantageBasketAdapter : IBasketSourceAdapter<AlphaVantageBasketAdapter.RawResult>
{
    public const string AdapterName = "alphavantage";
    public const string HttpClientName = "hqqq-refdata-basket-alphavantage";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BasketOptions _basketOptions;
    private readonly ILogger<AlphaVantageBasketAdapter> _logger;

    public AlphaVantageBasketAdapter(
        IHttpClientFactory httpClientFactory,
        IOptions<ReferenceDataOptions> options,
        ILogger<AlphaVantageBasketAdapter> logger)
    {
        _httpClientFactory = httpClientFactory;
        _basketOptions = options.Value.Basket;
        _logger = logger;
    }

    public string Name => AdapterName;

    public bool Enabled
    {
        get
        {
            var o = _basketOptions.Sources.AlphaVantage;
            // AlphaVantage requires a real API key; treat placeholder /
            // missing as effectively disabled so startup can still boot
            // on Nasdaq alone.
            return o.Enabled
                && !string.IsNullOrWhiteSpace(o.ApiKey)
                && !o.ApiKey.Contains("YOUR_", StringComparison.OrdinalIgnoreCase);
        }
    }

    public async Task<BasketSourceOutcome<RawResult>> FetchAsync(CancellationToken ct)
    {
        if (!Enabled)
            return BasketSourceOutcome<RawResult>.Disabled(AdapterName);

        var opts = _basketOptions.Sources.AlphaVantage;
        var url = $"{opts.BaseUrl.TrimEnd('/')}?function=ETF_PROFILE&symbol=QQQ&apikey={Uri.EscapeDataString(opts.ApiKey!)}";

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, opts.TimeoutSeconds));

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent", "hqqq-reference-data/1.0");

            using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return BasketSourceOutcome<RawResult>.Failed(
                    AdapterName, $"http status {(int)resp.StatusCode}");
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var payload = await JsonSerializer.DeserializeAsync<Wire>(stream, cancellationToken: ct)
                .ConfigureAwait(false);

            if (payload?.Holdings is null || payload.Holdings.Count == 0)
                return BasketSourceOutcome<RawResult>.Failed(AdapterName, "empty holdings");

            var rawCount = payload.Holdings.Count;
            var filtered = payload.Holdings
                .Where(IsValidEquityRow)
                .Select(h => new AlphaHolding(
                    (h.Symbol ?? string.Empty).Trim().ToUpperInvariant(),
                    (h.Description ?? "Unknown").Trim(),
                    ParseWeight(h.Weight)))
                .Where(h => !string.IsNullOrWhiteSpace(h.Symbol))
                .ToList();

            var sectors = (payload.Sectors ?? new List<WireSector>())
                .Select(s => new AlphaSector((s.Sector ?? "Unknown").Trim(), ParseWeight(s.Weight)))
                .ToList();

            decimal.TryParse(payload.NetAssets, CultureInfo.InvariantCulture, out var netAssets);

            var result = new RawResult
            {
                Holdings = filtered,
                Sectors = sectors,
                NetAssets = netAssets,
                RawCount = rawCount,
                FilteredCount = filtered.Count,
            };

            _logger.LogInformation(
                "AlphaVantageBasketAdapter: {Raw} raw -> {Filtered} filtered holdings, {Sectors} sectors",
                rawCount, filtered.Count, sectors.Count);

            return BasketSourceOutcome<RawResult>.Live(AdapterName, result, filtered.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AlphaVantageBasketAdapter: fetch failed");
            return BasketSourceOutcome<RawResult>.Failed(AdapterName, ex.Message);
        }
    }

    private static bool IsValidEquityRow(WireHolding h)
    {
        if (string.IsNullOrWhiteSpace(h.Symbol)
            || h.Symbol.Equals("n/a", StringComparison.OrdinalIgnoreCase))
            return false;

        var desc = (h.Description ?? string.Empty).ToUpperInvariant();
        if (desc.Contains("CASH") || desc.Contains("FUTURE") || desc.Contains("MONEY MARKET"))
            return false;

        return true;
    }

    private static decimal ParseWeight(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0m;
        return decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0m;
    }

    public sealed record RawResult
    {
        public required IReadOnlyList<AlphaHolding> Holdings { get; init; }
        public required IReadOnlyList<AlphaSector> Sectors { get; init; }
        public required decimal NetAssets { get; init; }
        public required int RawCount { get; init; }
        public required int FilteredCount { get; init; }
    }

    public sealed record AlphaHolding(string Symbol, string Description, decimal Weight);
    public sealed record AlphaSector(string Sector, decimal Weight);

    // ── Wire DTOs ──────────────────────────────────────────────

    internal sealed record Wire
    {
        [JsonPropertyName("net_assets")]
        public string? NetAssets { get; init; }

        [JsonPropertyName("holdings")]
        public List<WireHolding>? Holdings { get; init; }

        [JsonPropertyName("sectors")]
        public List<WireSector>? Sectors { get; init; }
    }

    internal sealed record WireHolding
    {
        [JsonPropertyName("symbol")] public string? Symbol { get; init; }
        [JsonPropertyName("description")] public string? Description { get; init; }
        [JsonPropertyName("weight")] public string? Weight { get; init; }
    }

    internal sealed record WireSector
    {
        [JsonPropertyName("sector")] public string? Sector { get; init; }
        [JsonPropertyName("weight")] public string? Weight { get; init; }
    }
}
