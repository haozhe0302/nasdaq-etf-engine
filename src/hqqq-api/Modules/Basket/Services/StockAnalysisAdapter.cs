using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Hqqq.Api.Modules.Basket.Services;

/// <summary>
/// Scrapes QQQ top holdings from stockanalysis.com/etf/qqq/holdings/.
///
/// Source characteristics:
///   • Server-rendered HTML with a Svelte-based table.
///   • Shows top 25 holdings (full list requires Pro subscription).
///   • Provides an explicit as-of date (e.g. "As of Mar 27, 2026").
///   • Fields: rank, symbol, name, % weight, shares.
///   • Preferred anchor source due to broader top-N coverage (25 vs 20)
///     and a clear holdings snapshot date.
///
/// Parser assumptions:
///   • Holdings table lives in a &lt;tbody&gt; with rows containing 5+ &lt;td&gt; cells.
///   • Symbol is in an &lt;a&gt; tag within the second cell.
///   • As-of date is extracted from "As of MMM dd, yyyy" text.
/// </summary>
public sealed class StockAnalysisAdapter
{
    private const string Url = "https://stockanalysis.com/etf/qqq/holdings/";
    private readonly HttpClient _httpClient;
    private readonly ILogger<StockAnalysisAdapter> _logger;

    public StockAnalysisAdapter(HttpClient httpClient, ILogger<StockAnalysisAdapter> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public sealed record StockAnalysisResult
    {
        public required List<ParsedHolding> Holdings { get; init; }
        public required DateOnly AsOfDate { get; init; }
        public required int TotalReported { get; init; }
    }

    public sealed record ParsedHolding(
        string Symbol, string Name, decimal WeightPct, decimal Shares);

    public async Task<StockAnalysisResult> FetchAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Fetching QQQ holdings from Stock Analysis");

        using var req = new HttpRequestMessage(HttpMethod.Get, Url);
        req.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        req.Headers.Add("Accept", "text/html");

        using var resp = await _httpClient.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();

        var html = await resp.Content.ReadAsStringAsync(ct);
        return Parse(html);
    }

    internal StockAnalysisResult Parse(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var asOf = ExtractAsOfDate(html);
        var total = ExtractTotal(html);
        var holdings = new List<ParsedHolding>();

        var tbodies = doc.DocumentNode.SelectNodes("//tbody");
        if (tbodies is null) return new() { Holdings = holdings, AsOfDate = asOf, TotalReported = total };

        foreach (var tbody in tbodies)
        {
            foreach (var tr in tbody.SelectNodes("tr") ?? Enumerable.Empty<HtmlNode>())
            {
                var tds = tr.SelectNodes("td");
                if (tds is null || tds.Count < 5) continue;

                var symbolNode = tds[1].SelectSingleNode(".//a");
                var symbol = symbolNode?.InnerText?.Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(symbol)) continue;

                var name = HtmlEntity.DeEntitize(tds[2].InnerText?.Trim() ?? "");
                var weightText = tds[3].InnerText?.Trim().Replace("%", "") ?? "";
                var sharesText = tds[4].InnerText?.Trim().Replace(",", "") ?? "";

                if (!decimal.TryParse(weightText, CultureInfo.InvariantCulture, out var w)) continue;
                decimal.TryParse(sharesText, CultureInfo.InvariantCulture, out var s);

                holdings.Add(new ParsedHolding(symbol, name, w, s));
            }
            if (holdings.Count > 0) break;
        }

        _logger.LogInformation("StockAnalysis: parsed {Count} holdings as of {AsOf}", holdings.Count, asOf);
        return new() { Holdings = holdings, AsOfDate = asOf, TotalReported = total };
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
}
