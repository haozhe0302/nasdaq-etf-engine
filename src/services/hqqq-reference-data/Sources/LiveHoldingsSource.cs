using System.Globalization;
using System.Net;
using System.Text.Json;
using Hqqq.ReferenceData.Configuration;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Sources;

/// <summary>
/// Real, minimal live-holdings source driven entirely by configuration:
/// <list type="bullet">
///   <item><c>None</c> — disabled; always returns <c>Unavailable</c>. The default demo posture.</item>
///   <item><c>File</c> — reads a JSON drop at <see cref="LiveHoldingsOptions.FilePath"/>.</item>
///   <item><c>Http</c> — issues a GET against <see cref="LiveHoldingsOptions.HttpUrl"/>.</item>
/// </list>
/// On any failure (missing config, missing file, non-2xx HTTP, malformed
/// JSON, asOfDate stale beyond <see cref="LiveHoldingsOptions.StaleAfterHours"/>)
/// the source returns <c>Unavailable</c> or <c>Invalid</c> with a reason —
/// it never throws, so the composite fallback runs cleanly.
/// </summary>
/// <remarks>
/// Provider-specific scrape adapters (Schwab / StockAnalysis / Nasdaq /
/// AlphaVantage) are out of scope for Phase 2 runtime. They exist in the
/// legacy monolith as reference only; if/when they are ported they plug
/// in behind <see cref="IHoldingsSource"/> without touching the refresh
/// pipeline.
/// </remarks>
public sealed class LiveHoldingsSource : IHoldingsSource
{
    private const string HttpClientName = "hqqq-refdata-live-holdings";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LiveHoldingsOptions _options;
    private readonly ILogger<LiveHoldingsSource> _logger;
    private readonly TimeProvider _clock;

    public LiveHoldingsSource(
        IHttpClientFactory httpClientFactory,
        IOptions<ReferenceDataOptions> options,
        ILogger<LiveHoldingsSource> logger,
        TimeProvider? clock = null)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value.LiveHoldings;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    public string Name => _options.SourceType switch
    {
        HoldingsSourceType.File => "live:file",
        HoldingsSourceType.Http => "live:http",
        _ => "live:none",
    };

    public async Task<HoldingsFetchResult> FetchAsync(CancellationToken ct)
    {
        switch (_options.SourceType)
        {
            case HoldingsSourceType.None:
                return HoldingsFetchResult.Unavailable("LiveHoldings.SourceType=None");

            case HoldingsSourceType.File:
                return await FetchFromFileAsync(ct).ConfigureAwait(false);

            case HoldingsSourceType.Http:
                return await FetchFromHttpAsync(ct).ConfigureAwait(false);

            default:
                return HoldingsFetchResult.Unavailable($"unknown SourceType {_options.SourceType}");
        }
    }

    private async Task<HoldingsFetchResult> FetchFromFileAsync(CancellationToken ct)
    {
        var path = _options.FilePath;
        if (string.IsNullOrWhiteSpace(path))
            return HoldingsFetchResult.Unavailable("LiveHoldings.FilePath is not configured");

        if (!File.Exists(path))
        {
            _logger.LogInformation(
                "LiveHoldingsSource(file): {Path} does not exist; reporting unavailable", path);
            return HoldingsFetchResult.Unavailable($"file not found: {path}");
        }

        string json;
        try
        {
            json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "LiveHoldingsSource(file): failed to read {Path}", path);
            return HoldingsFetchResult.Unavailable($"read failed: {ex.Message}");
        }

        return ParseAndProject(json, lineage: "live:file", originDescriptor: path);
    }

    private async Task<HoldingsFetchResult> FetchFromHttpAsync(CancellationToken ct)
    {
        var url = _options.HttpUrl;
        if (string.IsNullOrWhiteSpace(url))
            return HoldingsFetchResult.Unavailable("LiveHoldings.HttpUrl is not configured");

        var client = _httpClientFactory.CreateClient(HttpClientName);
        client.Timeout = TimeSpan.FromSeconds(Math.Max(1, _options.HttpTimeoutSeconds));

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(url, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "LiveHoldingsSource(http): GET {Url} failed", url);
            return HoldingsFetchResult.Unavailable($"http request failed: {ex.Message}");
        }

        using (response)
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                _logger.LogWarning(
                    "LiveHoldingsSource(http): GET {Url} returned {Status}", url, (int)response.StatusCode);
                return HoldingsFetchResult.Unavailable(
                    $"http status {(int)response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ParseAndProject(json, lineage: "live:http", originDescriptor: url);
        }
    }

    private HoldingsFetchResult ParseAndProject(string json, string lineage, string originDescriptor)
    {
        HoldingsFileSchema? file;
        try
        {
            file = JsonSerializer.Deserialize<HoldingsFileSchema>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "LiveHoldingsSource: malformed JSON from {Origin}", originDescriptor);
            return HoldingsFetchResult.Invalid($"malformed JSON: {ex.Message}");
        }

        if (file is null)
            return HoldingsFetchResult.Invalid("payload deserialized to null");

        if (string.IsNullOrWhiteSpace(file.AsOfDate)
            || !DateOnly.TryParseExact(file.AsOfDate, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var asOfDate))
        {
            return HoldingsFetchResult.Invalid(
                $"invalid asOfDate '{file.AsOfDate}' (expected yyyy-MM-dd)");
        }

        if (_options.StaleAfterHours > 0)
        {
            var ageHours = (_clock.GetUtcNow() - asOfDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc))
                .TotalHours;
            if (ageHours > _options.StaleAfterHours)
            {
                _logger.LogInformation(
                    "LiveHoldingsSource: snapshot asOf={AsOf} is {Hours:F1}h old (> {Limit}h); reporting unavailable",
                    asOfDate, ageHours, _options.StaleAfterHours);
                return HoldingsFetchResult.Unavailable(
                    $"snapshot stale by {ageHours:F1}h (limit {_options.StaleAfterHours}h)");
            }
        }

        var snapshot = new HoldingsSnapshot
        {
            BasketId = file.BasketId,
            Version = file.Version,
            AsOfDate = asOfDate,
            ScaleFactor = file.ScaleFactor,
            NavPreviousClose = file.NavPreviousClose,
            QqqPreviousClose = file.QqqPreviousClose,
            Constituents = file.Constituents
                .Select(c => new HoldingsConstituent
                {
                    Symbol = (c.Symbol ?? string.Empty).ToUpperInvariant(),
                    Name = c.Name ?? string.Empty,
                    Sector = c.Sector ?? string.Empty,
                    SharesHeld = c.SharesHeld,
                    ReferencePrice = c.ReferencePrice,
                    TargetWeight = c.TargetWeight,
                })
                .ToArray(),
            Source = lineage,
        };

        return HoldingsFetchResult.Ok(snapshot);
    }
}
