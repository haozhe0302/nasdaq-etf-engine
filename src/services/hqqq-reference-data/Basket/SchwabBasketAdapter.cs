using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Hqqq.ReferenceData.Configuration;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Basket;

/// <summary>
/// Phase 2 port of <c>src/hqqq-api/Modules/Basket/Services/SchwabHoldingsAdapter.cs</c>.
/// Scrapes QQQ holdings from Schwab's public ETF research page. Secondary
/// anchor source used when StockAnalysis is unavailable or its as-of
/// date is older.
/// </summary>
/// <remarks>
/// <para>
/// Fields scraped per row from the <c>tsraw</c>/<c>tsRaw</c> attributes
/// on <c>&lt;td&gt;</c> cells: symbol, description, % weight, shares
/// held, market value. As-of date comes from the embedded
/// <c>gHoldingsAsOfDate</c> JavaScript variable.
/// </para>
/// <para>
/// The page only renders the top ~20 constituents server-side; the rest
/// load via client-side AJAX and are therefore out of reach for a plain
/// HTTP client. The adapter accepts that limitation honestly — the
/// four-source merge relies on the tail block (AlphaVantage / Nasdaq)
/// to cover the remaining ~80 names.
/// </para>
/// </remarks>
public sealed class SchwabBasketAdapter : IBasketSourceAdapter<SchwabBasketAdapter.RawResult>
{
    public const string AdapterName = "schwab";
    public const string HttpClientName = "hqqq-refdata-basket-schwab";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BasketOptions _basketOptions;
    private readonly ILogger<SchwabBasketAdapter> _logger;

    public SchwabBasketAdapter(
        IHttpClientFactory httpClientFactory,
        IOptions<ReferenceDataOptions> options,
        ILogger<SchwabBasketAdapter> logger)
    {
        _httpClientFactory = httpClientFactory;
        _basketOptions = options.Value.Basket;
        _logger = logger;
    }

    public string Name => AdapterName;
    public bool Enabled => _basketOptions.Sources.Schwab.Enabled;

    public async Task<BasketSourceOutcome<RawResult>> FetchAsync(CancellationToken ct)
    {
        if (!Enabled)
            return BasketSourceOutcome<RawResult>.Disabled(AdapterName);

        var opts = _basketOptions.Sources.Schwab;

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
                "SchwabBasketAdapter: parsed {Scraped} of {Total} holdings as of {AsOf}",
                parsed.Holdings.Count, parsed.TotalReported, parsed.AsOfDate);

            return BasketSourceOutcome<RawResult>.Live(AdapterName, parsed, parsed.Holdings.Count);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SchwabBasketAdapter: fetch failed");
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
        var totalCount = ExtractTotalHoldings(html);
        var rows = ExtractRows(doc);

        return new RawResult
        {
            Holdings = rows,
            AsOfDate = asOf,
            TotalReported = totalCount,
        };
    }

    private static List<ParsedHolding> ExtractRows(HtmlDocument doc)
    {
        var tbody = doc.GetElementbyId("tthHoldingsTbody");
        if (tbody is null) return new List<ParsedHolding>();

        var rows = new List<ParsedHolding>();
        foreach (var tr in tbody.SelectNodes("tr") ?? Enumerable.Empty<HtmlNode>())
        {
            var tds = tr.SelectNodes("td");
            if (tds is null || tds.Count < 5) continue;

            var symbol = TsRaw(tds[0]);
            var desc = TsRaw(tds[1]);
            var weightStr = TsRaw(tds[2]);
            var sharesStr = TsRaw(tds[3]);
            var mktValStr = TsRaw(tds[4]);

            if (string.IsNullOrWhiteSpace(symbol)) continue;

            rows.Add(new ParsedHolding(
                symbol.Trim().ToUpperInvariant(),
                desc?.Trim() ?? "Unknown",
                ParseDecimal(weightStr),
                ParseDecimal(sharesStr),
                ParseDecimal(mktValStr)));
        }

        return rows;
    }

    private static string? TsRaw(HtmlNode td)
    {
        var val = td.GetAttributeValue("tsraw", string.Empty);
        if (string.IsNullOrEmpty(val))
            val = td.GetAttributeValue("tsRaw", string.Empty);
        return string.IsNullOrEmpty(val) ? null : val;
    }

    private static DateOnly ExtractAsOfDate(string html)
    {
        var match = Regex.Match(html, @"gHoldingsAsOfDate\s*=\s*'([^']+)'");
        if (match.Success &&
            DateOnly.TryParseExact(match.Groups[1].Value, "MM/dd/yyyy",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;

        return DateOnly.FromDateTime(DateTime.UtcNow);
    }

    private static int ExtractTotalHoldings(string html)
    {
        var match = Regex.Match(html, @"of\s+(\d+)\s+matches");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var n))
            return n;

        match = Regex.Match(html, @"(\d+)\s+Total\s+Holdings");
        return match.Success && int.TryParse(match.Groups[1].Value, out n) ? n : 0;
    }

    private static decimal ParseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0m;
        var cleaned = raw.Replace(",", string.Empty)
                         .Replace("$", string.Empty)
                         .Replace("%", string.Empty)
                         .Trim();
        return decimal.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0m;
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
        string Symbol, string Description,
        decimal WeightPct, decimal SharesHeld, decimal MarketValue);
}
