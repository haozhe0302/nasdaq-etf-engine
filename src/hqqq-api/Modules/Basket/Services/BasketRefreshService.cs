using Hqqq.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Hqqq.Api.Modules.Basket.Services;

/// <summary>
/// Background service with three distinct scheduled events:
///   08:00 ET — Fetch raw sources (save to per-source caches).
///   08:30 ET — Merge cached sources into a candidate basket (becomes pending).
///   09:30 ET — Activate pending basket (promotion only, no fetch/merge).
///
/// On startup: full refresh (fetch + merge) immediately.
/// Market-open wakeup is for activation ONLY — it does not trigger a new fetch.
/// </summary>
public sealed class BasketRefreshService : BackgroundService
{
    private readonly BasketSnapshotProvider _provider;
    private readonly TimeOnly _fetchTime;
    private readonly TimeOnly _mergeTime;
    private readonly TimeZoneInfo _tz;
    private readonly MarketHoursHelper _market;
    private readonly ILogger<BasketRefreshService> _logger;

    private static readonly TimeOnly MarketOpen = new(9, 30);

    public BasketRefreshService(
        BasketSnapshotProvider provider,
        IOptions<BasketOptions> basketOpts,
        IOptions<PricingOptions> pricingOpts,
        ILogger<BasketRefreshService> logger)
    {
        _provider = provider;
        _logger = logger;

        _fetchTime = TimeOnly.TryParse(basketOpts.Value.RefreshTimeLocal, out var t)
            ? t : new TimeOnly(8, 0);
        _mergeTime = new TimeOnly(_fetchTime.Hour, 30);

        _tz = TimeZoneInfo.FindSystemTimeZoneById(pricingOpts.Value.MarketTimeZone);
        _market = new MarketHoursHelper(_tz);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BasketRefreshService starting — initial full refresh");
        await SafeAsync(() => _provider.RefreshAsync(stoppingToken));

        while (!stoppingToken.IsCancellationRequested)
        {
            var (delay, evt) = ComputeNextEvent();
            _logger.LogInformation("Basket scheduler: next event={Event} in {Delay}", evt, delay);

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) { break; }

            switch (evt)
            {
                case ScheduleEvent.Fetch:
                    _logger.LogInformation("Scheduled raw-source fetch");
                    await SafeAsync(() => _provider.FetchRawSourcesAsync(stoppingToken));
                    break;

                case ScheduleEvent.Merge:
                    _logger.LogInformation("Scheduled merge from cached sources");
                    await SafeAsync(() => _provider.MergeAndApplyAsync(stoppingToken));
                    break;

                case ScheduleEvent.Activate:
                    if (_provider.GetState().Pending is not null && _market.IsMarketOpen())
                    {
                        _logger.LogInformation("Market open — activating pending basket");
                        _provider.ActivatePendingIfReady();
                    }
                    break;
            }
        }
    }

    private async Task SafeAsync(Func<Task> action)
    {
        try { await action(); }
        catch (Exception ex) { _logger.LogError(ex, "Basket scheduled action failed"); }
    }

    private enum ScheduleEvent { Fetch, Merge, Activate }

    private (TimeSpan Delay, ScheduleEvent Event) ComputeNextEvent()
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var nowLocal = TimeZoneInfo.ConvertTime(nowUtc, _tz);
        var today = nowLocal.Date;

        var candidates = new List<(DateTime Local, ScheduleEvent Event)>
        {
            (today + _fetchTime.ToTimeSpan(), ScheduleEvent.Fetch),
            (today + _mergeTime.ToTimeSpan(), ScheduleEvent.Merge),
        };

        if (_provider.GetState().Pending is not null)
        {
            var openCandidate = today + MarketOpen.ToTimeSpan();
            candidates.Add((openCandidate, ScheduleEvent.Activate));
        }

        var futureOnly = candidates
            .Select(c =>
            {
                var local = c.Local;
                if (nowLocal.DateTime >= local)
                    local = local.AddDays(1);
                while (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                    local = local.AddDays(1);
                return (Local: local, c.Event);
            })
            .OrderBy(c => c.Local)
            .First();

        var nextUtc = ToUtc(futureOnly.Local);
        var delay = nextUtc - nowUtc;
        return (delay > TimeSpan.Zero ? delay : TimeSpan.FromMinutes(1), futureOnly.Event);
    }

    private DateTimeOffset ToUtc(DateTime localUnspecified)
    {
        var dt = DateTime.SpecifyKind(localUnspecified, DateTimeKind.Unspecified);
        return new DateTimeOffset(dt, _tz.GetUtcOffset(dt)).ToUniversalTime();
    }
}
