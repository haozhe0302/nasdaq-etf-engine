using Hqqq.Api.Configuration;
using Hqqq.Api.Modules.Basket.Contracts;
using Microsoft.Extensions.Options;

namespace Hqqq.Api.Modules.Basket.Services;

/// <summary>
/// Background service that:
///   1. Refreshes the basket immediately on startup.
///   2. Refreshes daily at the configured local market time (default 08:00 ET).
///   3. Promotes pending baskets to active at market open (09:30 ET).
/// </summary>
public sealed class BasketRefreshService : BackgroundService
{
    private readonly IBasketSnapshotProvider _provider;
    private readonly TimeOnly _refreshTime;
    private readonly TimeZoneInfo _tz;
    private readonly MarketHoursHelper _market;
    private readonly ILogger<BasketRefreshService> _logger;

    private static readonly TimeOnly MarketOpen = new(9, 30);

    public BasketRefreshService(
        IBasketSnapshotProvider provider,
        IOptions<BasketOptions> basketOpts,
        IOptions<PricingOptions> pricingOpts,
        ILogger<BasketRefreshService> logger)
    {
        _provider = provider;
        _logger = logger;

        _refreshTime = TimeOnly.TryParse(basketOpts.Value.RefreshTimeLocal, out var t)
            ? t
            : new TimeOnly(8, 0);

        _tz = TimeZoneInfo.FindSystemTimeZoneById(pricingOpts.Value.MarketTimeZone);
        _market = new MarketHoursHelper(_tz);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BasketRefreshService starting — initial refresh");
        await SafeRefreshAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = ComputeNextWakeup();
            _logger.LogInformation("Basket service sleeping for {Delay}", delay);

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }

            TryActivatePending();
            await SafeRefreshAsync(stoppingToken);
        }
    }

    private void TryActivatePending()
    {
        var state = _provider.GetState();
        if (state.Pending is not null && _market.IsMarketOpen())
        {
            _logger.LogInformation("Market open — activating pending basket");
            _provider.ActivatePendingIfReady();
        }
    }

    private async Task SafeRefreshAsync(CancellationToken ct)
    {
        try { await _provider.RefreshAsync(ct); }
        catch (Exception ex) { _logger.LogError(ex, "Scheduled basket refresh failed"); }
    }

    /// <summary>
    /// Wakes at whichever comes first: the daily refresh time or market open
    /// (if there is a pending basket).
    /// </summary>
    private TimeSpan ComputeNextWakeup()
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, _tz);
        var today = nowLocal.Date;

        var refreshCandidate = today + _refreshTime.ToTimeSpan();
        if (nowLocal.DateTime >= refreshCandidate)
            refreshCandidate = refreshCandidate.AddDays(1);

        var nextRefreshUtc = ToUtc(refreshCandidate);

        var state = _provider.GetState();
        if (state.Pending is not null)
        {
            var openCandidate = today + MarketOpen.ToTimeSpan();
            if (nowLocal.DateTime >= openCandidate)
                openCandidate = openCandidate.AddDays(1);
            while (openCandidate.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                openCandidate = openCandidate.AddDays(1);

            var nextOpenUtc = ToUtc(openCandidate);
            if (nextOpenUtc < nextRefreshUtc)
                nextRefreshUtc = nextOpenUtc;
        }

        var delay = nextRefreshUtc - nowUtc;
        return delay > TimeSpan.Zero ? delay : TimeSpan.FromMinutes(1);
    }

    private DateTimeOffset ToUtc(DateTime localUnspecified)
    {
        var dt = DateTime.SpecifyKind(localUnspecified, DateTimeKind.Unspecified);
        return new DateTimeOffset(dt, _tz.GetUtcOffset(dt)).ToUniversalTime();
    }
}
