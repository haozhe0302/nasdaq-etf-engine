using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Hqqq.Api.Modules.Basket.Contracts;

namespace Hqqq.Api.Modules.Basket.Services;

/// <summary>
/// Scrapes QQQ ETF holdings from the public Schwab "All Holdings" page.
///
/// Source selection rationale:
///   Schwab hosts a publicly accessible ETF research page that displays
///   disclosed QQQ holdings with portfolio weights, shares held, market
///   value, and an as-of date. Unlike the Invesco download endpoint
///   (which requires JavaScript rendering), the Schwab page delivers
///   server-rendered HTML with structured <c>tsraw</c> attributes on
///   each table cell, making it reliably parseable with a standard HTTP
///   GET and an HTML parser.
///
/// Fields scraped per row (from <c>tsraw</c> attributes on &lt;td&gt; cells):
///   Column 0 — Symbol       (class "symbol firstColumn")
///   Column 1 — Description  (class "description")
///   Column 2 — % Portfolio Weight (sortname "PctNetAssets")
///   Column 3 — Shares Held  (sortname "SharesHeld")
///   Column 4 — Market Value (sortname "MarketValue")
///
/// As-of date:
///   Extracted from the JavaScript variable <c>gHoldingsAsOfDate</c>
///   embedded in the page footer, e.g. <c>var gHoldingsAsOfDate = '03/27/2026';</c>
///
/// Pagination limitation:
///   The page returns the top 20 holdings sorted by weight. Remaining
///   holdings are loaded via client-side AJAX (WSOD ModuleAPI) which
///   requires browser-side JavaScript session state and is not accessible
///   from a simple HTTP client. Browser automation is out of scope.
///   The adapter therefore returns only the top ~20 constituents that
///   the server renders in the initial HTML response.
/// </summary>
public sealed class SchwabHoldingsAdapter
{
    private const string DefaultSchwabUrl =
        "https://www.schwab.wallst.com/schwab/Prospect/research/etfs/schwabETF/index.asp?type=holdings&symbol=QQQ";

    private readonly HttpClient _httpClient;
    private readonly ILogger<SchwabHoldingsAdapter> _logger;

    public SchwabHoldingsAdapter(
        HttpClient httpClient,
        ILogger<SchwabHoldingsAdapter> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public sealed record SchwabFetchResult
    {
        public required IReadOnlyList<BasketConstituent> Constituents { get; init; }
        public required DateOnly AsOfDate { get; init; }
        public required int TotalHoldingsReported { get; init; }
    }

    public async Task<SchwabFetchResult> FetchAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Fetching QQQ holdings from Schwab");

        using var request = new HttpRequestMessage(HttpMethod.Get, DefaultSchwabUrl);
        request.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        request.Headers.Add("Accept", "text/html");

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync(ct);
        return Parse(html);
    }

    internal SchwabFetchResult Parse(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var asOfDate = ExtractAsOfDate(html);
        var totalCount = ExtractTotalHoldings(html);
        var rows = ExtractRows(doc);

        _logger.LogInformation(
            "Schwab: parsed {Scraped} of {Total} holdings as of {AsOf}",
            rows.Count, totalCount, asOfDate);

        var constituents = rows.Select(r => new BasketConstituent
        {
            Symbol = r.Symbol,
            SecurityName = r.Description,
            Exchange = "NASDAQ",
            Currency = "USD",
            Weight = r.WeightPct / 100m,
            SharesHeld = r.SharesHeld,
            Sector = "Unknown",
            AsOfDate = asOfDate,
        }).ToList();

        return new SchwabFetchResult
        {
            Constituents = constituents,
            AsOfDate = asOfDate,
            TotalHoldingsReported = totalCount,
        };
    }

    #region HTML parsing (isolated)

    private sealed record ParsedRow(
        string Symbol, string Description,
        decimal WeightPct, decimal SharesHeld, decimal MarketValue);

    private static List<ParsedRow> ExtractRows(HtmlDocument doc)
    {
        var tbody = doc.GetElementbyId("tthHoldingsTbody");
        if (tbody is null) return [];

        var rows = new List<ParsedRow>();
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

            rows.Add(new ParsedRow(
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
        var val = td.GetAttributeValue("tsraw", "");
        if (string.IsNullOrEmpty(val))
            val = td.GetAttributeValue("tsRaw", "");
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
        if (match.Success && int.TryParse(match.Groups[1].Value, out n))
            return n;

        return 0;
    }

    private static decimal ParseDecimal(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0m;
        var cleaned = raw.Replace(",", "").Replace("$", "").Replace("%", "").Trim();
        return decimal.TryParse(cleaned, CultureInfo.InvariantCulture, out var v) ? v : 0m;
    }

    #endregion
}
