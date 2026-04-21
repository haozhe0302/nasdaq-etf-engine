using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Hqqq.ReferenceData.Configuration;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Basket;

/// <summary>
/// Phase 2 port of <c>src/hqqq-api/Modules/Basket/Services/StockAnalysisAdapter.cs</c>.
/// Scrapes QQQ top holdings from stockanalysis.com. Preferred anchor
/// source because it exposes authoritative <c>SharesHeld</c> and a
/// clear as-of date for the holdings snapshot.
/// </summary>
/// <remarks>
/// <para>
/// Source characteristics:
/// <list type="bullet">
///   <item>Server-rendered HTML with a Svelte-based table.</item>
///   <item>Typically shows the top 25 holdings (full list requires a Pro subscription).</item>
///   <item>Provides an explicit as-of date ("As of MMM dd, yyyy").</item>
///   <item>Row fields: rank, symbol, name, % weight, shares.</item>
/// </list>
/// </para>
/// <para>
/// The adapter never throws. All failures (network, non-2xx, parse,
/// empty table) are surfaced as <see cref="BasketSourceOutcome{T}"/>
/// with <c>Success=false</c> so the composite can fall through.
/// </para>
/// </remarks>
public sealed class StockAnalysisBasketAdapter : IBasketSourceAdapter<StockAnalysisBasketAdapter.RawResult>
{
    public const string AdapterName = "stockanalysis";
    public const string HttpClientName = "hqqq-refdata-basket-stockanalysis";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BasketOptions _basketOptions;
    private readonly ILogger<StockAnalysisBasketAdapter> _logger;

    public StockAnalysisBasketAdapter(
        IHttpClientFactory httpClientFactory,
        IOptions<ReferenceDataOptions> options,
        ILogger<StockAnalysisBasketAdapter> logger)
    {
        _httpClientFactory = httpClientFactory;
        _basketOptions = options.Value.Basket;
        _logger = logger;
    }

    public string Name => AdapterName;
    public bool Enabled => _basketOptions.Sources.StockAnalysis.Enabled;

    public async Task<BasketSourceOutcome<RawResult>> FetchAsync(CancellationToken ct)
    {
        if (!Enabled)
            return BasketSourceOutcome<RawResult>.Disabled(AdapterName);

        var opts = _basketOptions.Sources.StockAnalysis;

        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, opts.TimeoutSeconds));

            using var req = new HttpRequestMessage(HttpMethod.Get, opts.Url);
            req.Headers.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/124.0.0.0 Safari/537.36");
            req.Headers.TryAddWithoutValidation("Accept", "text/html");

            using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return BasketSourceOutcome<RawResult>.Failed(
                    AdapterName, $"http status {(int)resp.StatusCode}");
            }

            var html = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var parsed = Parse(html);

            if (parsed.Holdings.Count == 0)
                return BasketSourceOutcome<RawResult>.Failed(AdapterName, "no holdings parsed from HTML");

            _logger.LogInformation(
                "StockAnalysisBasketAdapter: parsed {Count} holdings as of {AsOf}",
                parsed.Holdings.Count, parsed.AsOfDate);

            return BasketSourceOutcome<RawResult>.Live(AdapterName, parsed, parsed.Holdings.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "StockAnalysisBasketAdapter: fetch failed");
            return BasketSourceOutcome<RawResult>.Failed(AdapterName, ex.Message);
        }
    }

    /// <summary>
    /// HTML parser exposed as <c>internal</c> so unit tests can pin DOM
    /// behaviour without spinning up a live network request.
    /// </summary>
    internal static RawResult Parse(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var asOf = ExtractAsOfDate(html);
        var total = ExtractTotal(html);
        var holdings = new List<ParsedHolding>();

        var tbodies = doc.DocumentNode.SelectNodes("//tbody");
        if (tbodies is null)
            return new RawResult { Holdings = holdings, AsOfDate = asOf, TotalReported = total };

        foreach (var tbody in tbodies)
        {
            foreach (var tr in tbody.SelectNodes("tr") ?? Enumerable.Empty<HtmlNode>())
            {
                var tds = tr.SelectNodes("td");
                if (tds is null || tds.Count < 5) continue;

                var symbolNode = tds[1].SelectSingleNode(".//a");
                var symbol = symbolNode?.InnerText?.Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(symbol)) continue;

                var name = HtmlEntity.DeEntitize(tds[2].InnerText?.Trim() ?? string.Empty);
                var weightText = tds[3].InnerText?.Trim().Replace("%", string.Empty) ?? string.Empty;
                var sharesText = tds[4].InnerText?.Trim().Replace(",", string.Empty) ?? string.Empty;

                if (!decimal.TryParse(weightText, NumberStyles.Float, CultureInfo.InvariantCulture, out var w))
                    continue;
                decimal.TryParse(sharesText, NumberStyles.Float, CultureInfo.InvariantCulture, out var s);

                holdings.Add(new ParsedHolding(symbol, name, w, s));
            }
            if (holdings.Count > 0) break;
        }

        return new RawResult { Holdings = holdings, AsOfDate = asOf, TotalReported = total };
    }

    private static DateOnly ExtractAsOfDate(string html)
    {
        var m = Regex.Match(html, @"As of ([A-Za-z]+ \d{1,2}, \d{4})");
        if (m.Success && DateOnly.TryParseExact(m.Groups[1].Value, "MMM d, yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;

        if (m.Success && DateOnly.TryParse(m.Groups[1].Value, CultureInfo.InvariantCulture, out d))
            return d;

        return DateOnly.FromDateTime(DateTime.UtcNow);
    }

    private static int ExtractTotal(string html)
    {
        var m = Regex.Match(html, @"(\d+) of (\d+) holdings");
        return m.Success && int.TryParse(m.Groups[2].Value, out var n) ? n : 0;
    }

    public sealed record RawResult
    {
        [JsonPropertyName("holdings")]
        public required IReadOnlyList<ParsedHolding> Holdings { get; init; }

        [JsonPropertyName("asOfDate")]
        public required DateOnly AsOfDate { get; init; }

        [JsonPropertyName("totalReported")]
        public required int TotalReported { get; init; }
    }

    public sealed record ParsedHolding(
        string Symbol, string Name, decimal WeightPct, decimal Shares);
}
