using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Hqqq.Api.Configuration;
using Hqqq.Api.Modules.CorporateActions.Contracts;

namespace Hqqq.Api.Modules.CorporateActions.Services;

/// <summary>
/// Fetches stock-split data from the Tiingo end-of-day prices endpoint
/// (<c>/tiingo/daily/{ticker}/prices</c>) and extracts rows where
/// <c>splitFactor ≠ 1</c>.
/// <list type="bullet">
///   <item>Per-symbol in-memory cache with configurable TTL (default 1 h).</item>
///   <item>Concurrency-limited to avoid overwhelming the upstream API.</item>
///   <item>Per-symbol failures are isolated — one failing ticker does not
///         prevent results for others.</item>
/// </list>
/// </summary>
public sealed class TiingoCorporateActionProvider : ICorporateActionProvider
{
    private const string BaseUrl = "https://api.tiingo.com/tiingo/daily";
    private const int MaxConcurrency = 5;

    private readonly IHttpClientFactory _httpFactory;
    private readonly TiingoOptions _tiingoOptions;
    private readonly ILogger<TiingoCorporateActionProvider> _logger;

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);
    private readonly SemaphoreSlim _throttle = new(MaxConcurrency);

    private sealed record CacheEntry(
        DateTimeOffset FetchedAt,
        DateOnly FromDate,
        DateOnly ToDate,
        IReadOnlyList<SplitEvent> Splits);

    public TiingoCorporateActionProvider(
        IHttpClientFactory httpFactory,
        IOptions<TiingoOptions> tiingoOptions,
        ILogger<TiingoCorporateActionProvider> logger)
    {
        _httpFactory = httpFactory;
        _tiingoOptions = tiingoOptions.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SplitEvent>> GetSplitsAsync(
        IEnumerable<string> symbols,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken ct = default)
    {
        var tasks = symbols
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(s => GetSplitsForSymbolAsync(s, fromDate, toDate, ct));

        var results = await Task.WhenAll(tasks);
        return results.SelectMany(r => r).ToList();
    }

    private async Task<IReadOnlyList<SplitEvent>> GetSplitsForSymbolAsync(
        string symbol, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        if (_cache.TryGetValue(symbol, out var entry)
            && entry.FetchedAt + CacheTtl > DateTimeOffset.UtcNow
            && entry.FromDate <= fromDate
            && entry.ToDate >= toDate)
        {
            return entry.Splits
                .Where(s => s.EffectiveDate >= fromDate && s.EffectiveDate <= toDate)
                .ToList();
        }

        await _throttle.WaitAsync(ct);
        try
        {
            var splits = await FetchFromTiingoAsync(symbol, fromDate, toDate, ct);
            _cache[symbol] = new CacheEntry(DateTimeOffset.UtcNow, fromDate, toDate, splits);
            return splits;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to fetch split data for {Symbol} from Tiingo", symbol);
            return [];
        }
        finally
        {
            _throttle.Release();
        }
    }

    private async Task<IReadOnlyList<SplitEvent>> FetchFromTiingoAsync(
        string symbol, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        var ticker = symbol.ToLowerInvariant();
        var url = $"{BaseUrl}/{ticker}/prices" +
                  $"?startDate={fromDate:yyyy-MM-dd}" +
                  $"&endDate={toDate:yyyy-MM-dd}" +
                  $"&format=json" +
                  $"&token={_tiingoOptions.ApiKey}";

        using var http = _httpFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(10);

        var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        return ParseSplits(symbol, json);
    }

    private IReadOnlyList<SplitEvent> ParseSplits(string symbol, string json)
    {
        var splits = new List<SplitEvent>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return splits;

            foreach (var row in doc.RootElement.EnumerateArray())
            {
                if (!row.TryGetProperty("splitFactor", out var sfProp)) continue;

                decimal sf;
                if (sfProp.ValueKind == JsonValueKind.Number)
                    sf = sfProp.GetDecimal();
                else
                    continue;

                if (sf == 1.0m) continue;

                var dateStr = row.TryGetProperty("date", out var dateProp)
                    ? dateProp.GetString() : null;
                if (dateStr is null || dateStr.Length < 10) continue;
                if (!DateOnly.TryParse(dateStr.AsSpan(0, 10), out var date)) continue;

                var description = sf > 1m
                    ? $"{sf}:1 forward split"
                    : $"1:{Math.Round(1m / sf, 2)} reverse split";

                splits.Add(new SplitEvent
                {
                    Symbol = symbol.ToUpperInvariant(),
                    EffectiveDate = date,
                    Factor = sf,
                    Description = description,
                    Source = "tiingo",
                });
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Tiingo daily response for {Symbol}", symbol);
        }

        return splits;
    }
}
