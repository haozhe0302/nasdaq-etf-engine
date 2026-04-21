using System.Collections.Concurrent;
using System.Text.Json;
using Hqqq.ReferenceData.Configuration;
using Hqqq.ReferenceData.CorporateActions.Contracts;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.CorporateActions.Providers;

/// <summary>
/// Real Tiingo EOD corporate-action provider. Fetches split data from
/// <c>{BaseUrl}/{ticker}/prices</c> and extracts rows where
/// <c>splitFactor ≠ 1</c>. Only active when
/// <c>ReferenceData:CorporateActions:Tiingo:Enabled=true</c>.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>Per-symbol in-memory cache with TTL
///         (<see cref="TiingoCorporateActionOptions.CacheTtlMinutes"/>).</item>
///   <item>Concurrency-limited
///         (<see cref="TiingoCorporateActionOptions.MaxConcurrency"/>) to avoid
///         overwhelming Tiingo.</item>
///   <item>Per-symbol failures are isolated — one failing ticker does not
///         prevent results for others. Fatal errors surface on
///         <see cref="CorporateActionFeed.Error"/>.</item>
///   <item>Does not emit rename events — Tiingo's daily endpoint does
///         not carry ticker remap metadata. Renames come from the file
///         provider only.</item>
/// </list>
/// This class structurally mirrors the legacy
/// <c>Hqqq.Api.Modules.CorporateActions.Services.TiingoCorporateActionProvider</c>
/// as a reference, but does not import any monolith types at compile or
/// runtime.
/// </remarks>
public sealed class TiingoCorporateActionProvider : ICorporateActionProvider
{
    /// <summary>Named HttpClient used for the Tiingo EOD calls.</summary>
    public const string HttpClientName = "hqqq-refdata-corp-actions";

    private readonly IHttpClientFactory _httpFactory;
    private readonly TiingoCorporateActionOptions _options;
    private readonly ILogger<TiingoCorporateActionProvider> _logger;

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _throttle;

    private sealed record CacheEntry(
        DateTimeOffset FetchedAt,
        DateOnly FromDate,
        DateOnly ToDate,
        IReadOnlyList<SplitEvent> Splits);

    public TiingoCorporateActionProvider(
        IHttpClientFactory httpFactory,
        IOptions<ReferenceDataOptions> options,
        ILogger<TiingoCorporateActionProvider> logger)
    {
        _httpFactory = httpFactory;
        _options = options.Value.CorporateActions.Tiingo;
        _logger = logger;
        _throttle = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrency));
    }

    public string Name => "tiingo";

    public async Task<CorporateActionFeed> FetchAsync(
        IReadOnlyCollection<string> symbols,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            return CorporateActionFeed.Empty(Name) with
            {
                Error = "Tiingo corp-action provider disabled (CorporateActions:Tiingo:Enabled=false)",
            };
        }

        if (!HasUsableApiKey(_options.ApiKey))
        {
            return CorporateActionFeed.Empty(Name) with
            {
                Error = "Tiingo corp-action provider enabled but CorporateActions:Tiingo:ApiKey is missing/placeholder",
            };
        }

        if (symbols.Count == 0)
        {
            return CorporateActionFeed.Empty(Name);
        }

        var uniqueSymbols = symbols
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var tasks = uniqueSymbols
            .Select(s => GetSplitsForSymbolAsync(s, from, to, ct))
            .ToArray();

        IReadOnlyList<SplitEvent>[] results;
        try
        {
            results = await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }

        var flat = results.SelectMany(r => r).ToList();
        return new CorporateActionFeed
        {
            Splits = flat,
            Renames = Array.Empty<SymbolRenameEvent>(),
            Source = Name,
            FetchedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private async Task<IReadOnlyList<SplitEvent>> GetSplitsForSymbolAsync(
        string symbol, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var ttl = TimeSpan.FromMinutes(Math.Max(1, _options.CacheTtlMinutes));

        if (_cache.TryGetValue(symbol, out var entry)
            && entry.FetchedAt + ttl > DateTimeOffset.UtcNow
            && entry.FromDate <= from
            && entry.ToDate >= to)
        {
            return entry.Splits
                .Where(s => s.EffectiveDate >= from && s.EffectiveDate <= to)
                .ToList();
        }

        await _throttle.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var splits = await FetchFromTiingoAsync(symbol, from, to, ct).ConfigureAwait(false);
            _cache[symbol] = new CacheEntry(DateTimeOffset.UtcNow, from, to, splits);
            return splits;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "TiingoCorporateActionProvider: failed to fetch splits for {Symbol}", symbol);
            return Array.Empty<SplitEvent>();
        }
        finally
        {
            _throttle.Release();
        }
    }

    private async Task<IReadOnlyList<SplitEvent>> FetchFromTiingoAsync(
        string symbol, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var ticker = symbol.ToLowerInvariant();
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? "https://api.tiingo.com/tiingo/daily"
            : _options.BaseUrl.TrimEnd('/');
        var url = $"{baseUrl}/{ticker}/prices" +
                  $"?startDate={from:yyyy-MM-dd}" +
                  $"&endDate={to:yyyy-MM-dd}" +
                  $"&format=json" +
                  $"&token={_options.ApiKey}";

        using var http = _httpFactory.CreateClient(HttpClientName);
        http.Timeout = TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds));

        var response = await http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
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
                if (!row.TryGetProperty("splitFactor", out var sfProp)
                    || sfProp.ValueKind != JsonValueKind.Number) continue;

                var sf = sfProp.GetDecimal();
                if (sf <= 0m || sf == 1m) continue;

                var dateStr = row.TryGetProperty("date", out var dateProp) ? dateProp.GetString() : null;
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
                    Source = Name,
                });
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "TiingoCorporateActionProvider: failed to parse response for {Symbol}", symbol);
        }

        return splits;
    }

    private static bool HasUsableApiKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        var lowered = key.Trim().ToLowerInvariant();
        return !(lowered.Contains("<set")
            || lowered.Contains("your_")
            || lowered.Contains("changeme")
            || lowered.Contains("replace_me"));
    }
}
