using System.Globalization;
using Hqqq.ReferenceData.Configuration;
using Hqqq.ReferenceData.Services;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Basket;

/// <summary>
/// Phase 2 port of the Phase 1 <c>BasketRefreshService</c> scheduler.
/// Drives the three-stage daily lifecycle:
/// <list type="bullet">
///   <item><c>FetchTimeLocal</c> (default 08:00 ET) — pull AlphaVantage + Nasdaq into the raw cache.</item>
///   <item><c>MergeTimeLocal</c> (default 08:30 ET) — merge cached payloads into a pending basket.</item>
///   <item><c>ActivateTimeLocal</c> (default 09:30 ET) — if the market is open and the pending fingerprint differs from the active, run <see cref="BasketRefreshPipeline.RefreshAsync"/> to publish.</item>
/// </list>
/// Only runs when <see cref="BasketOptions.Mode"/> is
/// <see cref="BasketMode.RealSource"/>; on Seed mode the scheduler is
/// registered but no-ops so the existing <c>BasketRefreshJob</c> drives
/// the seed pipeline unchanged.
/// </summary>
public sealed class BasketLifecycleScheduler : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    private readonly RealSourceBasketPipeline _pipeline;
    private readonly BasketRefreshPipeline _activation;
    private readonly PendingBasketStore _pending;
    private readonly ActiveBasketStore _active;
    private readonly MarketHoursHelper _market;
    private readonly BasketOptions _options;
    private readonly TimeProvider _clock;
    private readonly TimeZoneInfo _tz;
    private readonly ILogger<BasketLifecycleScheduler> _logger;

    private DateOnly? _lastFetchedDay;
    private DateOnly? _lastMergedDay;
    private DateOnly? _lastActivatedDay;

    public BasketLifecycleScheduler(
        RealSourceBasketPipeline pipeline,
        BasketRefreshPipeline activation,
        PendingBasketStore pending,
        ActiveBasketStore active,
        IOptions<ReferenceDataOptions> options,
        ILogger<BasketLifecycleScheduler> logger,
        TimeProvider? clock = null)
    {
        _pipeline = pipeline;
        _activation = activation;
        _pending = pending;
        _active = active;
        _options = options.Value.Basket;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
        _tz = MarketHoursHelper.ResolveTimeZone(_options.MarketTimeZone);
        _market = new MarketHoursHelper(_tz, _clock);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.Mode != BasketMode.RealSource)
        {
            _logger.LogInformation(
                "BasketLifecycleScheduler: Mode={Mode}; scheduler is a no-op in this posture",
                _options.Mode);
            return;
        }

        if (!TryParseWallClock(_options.Schedule.FetchTimeLocal, out var fetchAt)
            || !TryParseWallClock(_options.Schedule.MergeTimeLocal, out var mergeAt)
            || !TryParseWallClock(_options.Schedule.ActivateTimeLocal, out var activateAt))
        {
            _logger.LogError(
                "BasketLifecycleScheduler: invalid schedule — fetch={Fetch} merge={Merge} activate={Activate}; exiting",
                _options.Schedule.FetchTimeLocal, _options.Schedule.MergeTimeLocal, _options.Schedule.ActivateTimeLocal);
            return;
        }

        _logger.LogInformation(
            "BasketLifecycleScheduler starting — zone={Zone} fetch={Fetch} merge={Merge} activate={Activate}",
            _tz.Id, fetchAt, mergeAt, activateAt);

        // Warm start: rehydrate pending from the merged cache if present.
        try
        {
            await _pipeline.RecoverFromCacheAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "BasketLifecycleScheduler: warm-start recovery failed; continuing");
        }

        using var timer = new PeriodicTimer(TickInterval);
        try
        {
            do
            {
                try
                {
                    await RunTickAsync(fetchAt, mergeAt, activateAt, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "BasketLifecycleScheduler: tick failed; continuing");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
    }

    private async Task RunTickAsync(
        TimeOnly fetchAt, TimeOnly mergeAt, TimeOnly activateAt,
        CancellationToken ct)
    {
        var nowLocal = TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), _tz);
        var today = DateOnly.FromDateTime(nowLocal.DateTime);
        var time = TimeOnly.FromDateTime(nowLocal.DateTime);

        // Fetch gate
        if (_lastFetchedDay != today && time >= fetchAt)
        {
            _logger.LogInformation("Lifecycle.Fetch: firing for {Day}", today);
            await _pipeline.FetchAsync(ct).ConfigureAwait(false);
            _lastFetchedDay = today;
        }

        // Merge gate
        if (_lastMergedDay != today && time >= mergeAt)
        {
            _logger.LogInformation("Lifecycle.Merge: firing for {Day}", today);
            await _pipeline.MergeAsync(ct).ConfigureAwait(false);
            _lastMergedDay = today;
        }

        // Activate gate — only when the market is open and there is a
        // pending basket whose fingerprint differs from the currently
        // active one.
        if (_lastActivatedDay != today && time >= activateAt)
        {
            if (!_market.IsMarketOpen())
            {
                // Not a market day / closed session — remember so we don't
                // spin on this branch all day, but do not rotate
                // `_lastActivatedDay` so we retry if the market reopens
                // (e.g. a scheduled pause).
                return;
            }

            var pending = _pending.Pending;
            if (pending is null)
            {
                _logger.LogInformation(
                    "Lifecycle.Activate: market open but no pending basket; deferring");
                return;
            }

            var activeFp16 = ComputeActiveFingerprint16(pending);
            if (activeFp16 == pending.ContentFingerprint16 && _active.Current is not null)
            {
                _logger.LogInformation(
                    "Lifecycle.Activate: pending matches active {Fp16}; no-op",
                    pending.ContentFingerprint16);
                _lastActivatedDay = today;
                return;
            }

            _logger.LogInformation(
                "Lifecycle.Activate: promoting pending {Fp16}",
                pending.ContentFingerprint16);

            var result = await _activation.RefreshAsync(ct).ConfigureAwait(false);
            if (!result.Success)
            {
                _logger.LogWarning(
                    "Lifecycle.Activate: refresh failed — {Error}; will retry on next tick",
                    result.Error);
                return;
            }

            _lastActivatedDay = today;
        }
    }

    private string? ComputeActiveFingerprint16(MergedBasketEnvelope pending)
    {
        var active = _active.Current;
        if (active is null) return null;
        return MergedBasketBuilder.ComputeContentFingerprint16(
            active.Snapshot.Constituents, active.Snapshot.AsOfDate);
    }

    private static bool TryParseWallClock(string input, out TimeOnly result)
    {
        return TimeOnly.TryParseExact(
            input,
            new[] { "HH:mm", "H:mm", "HH:mm:ss" },
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out result);
    }
}
