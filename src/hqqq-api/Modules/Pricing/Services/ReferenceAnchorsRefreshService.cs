using Microsoft.Extensions.Options;
using Hqqq.Api.Configuration;
using Hqqq.Api.Modules.History.Services;
using Hqqq.Api.Modules.MarketData.Services;
using Hqqq.Api.Modules.Pricing.Contracts;

namespace Hqqq.Api.Modules.Pricing.Services;

/// <summary>
/// Background service that refreshes <see cref="ReferenceAnchors"/> daily:
/// QQQ previous close from Tiingo REST and iNAV previous close from History.
/// Runs immediately on startup, then at a configurable ET time each day.
/// </summary>
public sealed class ReferenceAnchorsRefreshService : BackgroundService
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

    private readonly ReferenceAnchorsStore _store;
    private readonly TiingoRestClient _restClient;
    private readonly HistoryFileStore _historyStore;
    private readonly MarketSessionService _sessionService;
    private readonly PricingOptions _options;
    private readonly TimeZoneInfo _marketTz;
    private readonly ILogger<ReferenceAnchorsRefreshService> _logger;

    public ReferenceAnchorsRefreshService(
        ReferenceAnchorsStore store,
        TiingoRestClient restClient,
        HistoryFileStore historyStore,
        MarketSessionService sessionService,
        IOptions<PricingOptions> options,
        ILogger<ReferenceAnchorsRefreshService> logger)
    {
        _store = store;
        _restClient = restClient;
        _historyStore = historyStore;
        _sessionService = sessionService;
        _options = options.Value;
        _marketTz = ResolveTimeZone(_options.MarketTimeZone);
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ReferenceAnchors refresh service starting");

        await RefreshWithRetriesAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = ComputeDelayUntilNextRefresh();
            _logger.LogInformation(
                "Next anchor refresh in {Hours:F1}h ({Time} ET)",
                delay.TotalHours,
                TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.UtcNow.Add(delay), _marketTz).ToString("HH:mm"));

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            await RefreshWithRetriesAsync(stoppingToken);
        }
    }

    private async Task RefreshWithRetriesAsync(CancellationToken ct)
    {
        var today = GetMarketDate();

        var existing = _store.Get();
        if (existing is not null && existing.AnchorDate == today)
        {
            _logger.LogDebug("Anchors already fresh for {Date}, skipping", today);
            return;
        }

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var anchors = await BuildAnchorsAsync(today, ct);
                _store.Update(anchors);
                _logger.LogInformation(
                    "Reference anchors refreshed for {Date}: QQQ prevClose={Qqq}, iNAV prevClose={Nav}",
                    today,
                    anchors.QqqPreviousClose?.ToString("F2") ?? "n/a",
                    anchors.NavPreviousClose?.ToString("F2") ?? "n/a");
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex,
                    "Anchor refresh attempt {Attempt}/{Max} failed",
                    attempt, MaxRetries);

                if (attempt < MaxRetries)
                {
                    try { await Task.Delay(RetryDelay, ct); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                }
            }
        }

        _logger.LogError("All {Max} anchor refresh attempts failed", MaxRetries);
    }

    private async Task<ReferenceAnchors> BuildAnchorsAsync(DateOnly today, CancellationToken ct)
    {
        decimal? qqqPrevClose = null;
        decimal? navPrevClose = null;

        var ticks = await _restClient.FetchLatestPricesAsync(["QQQ"], ct);
        var qqqTick = ticks.FirstOrDefault(t =>
            string.Equals(t.Symbol, "QQQ", StringComparison.OrdinalIgnoreCase));

        if (qqqTick?.PreviousClose is > 0)
        {
            qqqPrevClose = qqqTick.PreviousClose;
            _logger.LogDebug("QQQ prevClose from Tiingo: {Price}", qqqPrevClose);
        }
        else
        {
            _logger.LogWarning("Tiingo did not return a prevClose for QQQ");
        }

        var prevTradingDay = FindPreviousTradingDay(today);
        if (prevTradingDay is not null)
        {
            var rows = _historyStore.LoadRange(prevTradingDay.Value, prevTradingDay.Value);
            if (rows.Count > 0)
            {
                navPrevClose = rows[^1].Nav;
                _logger.LogDebug(
                    "iNAV prevClose from history ({Date}): {Nav}",
                    prevTradingDay, navPrevClose);
            }
            else
            {
                _logger.LogWarning(
                    "No history data for previous trading day {Date}", prevTradingDay);
            }
        }

        return new ReferenceAnchors
        {
            QqqPreviousClose = qqqPrevClose,
            NavPreviousClose = navPrevClose,
            AnchorDate = today,
            RefreshedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    private DateOnly? FindPreviousTradingDay(DateOnly today)
    {
        var candidate = today.AddDays(-1);
        for (int i = 0; i < 14; i++)
        {
            var utc = new DateTimeOffset(
                candidate.ToDateTime(new TimeOnly(12, 0)),
                _marketTz.GetUtcOffset(candidate.ToDateTime(new TimeOnly(12, 0))));
            var session = _sessionService.GetSession(utc.UtcDateTime == default
                ? utc : utc);

            if (session.IsTradingDay)
                return candidate;

            candidate = candidate.AddDays(-1);
        }

        _logger.LogWarning("Could not find a previous trading day within 14 days");
        return null;
    }

    private TimeSpan ComputeDelayUntilNextRefresh()
    {
        if (!TimeOnly.TryParse(_options.AnchorRefreshTimeLocal, out var targetTime))
            targetTime = new TimeOnly(9, 0);

        var nowUtc = DateTime.UtcNow;
        var nowEt = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, _marketTz);
        var todayTarget = nowEt.Date + targetTime.ToTimeSpan();

        var nextEt = nowEt < todayTarget ? todayTarget : todayTarget.AddDays(1);
        var nextUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(nextEt, DateTimeKind.Unspecified), _marketTz);

        var delay = nextUtc - nowUtc;
        return delay > TimeSpan.Zero ? delay : TimeSpan.FromMinutes(1);
    }

    private DateOnly GetMarketDate()
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _marketTz);
        return DateOnly.FromDateTime(local);
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) when (id == "America/New_York")
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        }
    }
}
