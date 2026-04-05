using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Hqqq.Api.Configuration;
using Hqqq.Api.Hubs;
using Hqqq.Api.Modules.Benchmark.Services;
using Hqqq.Api.Modules.Pricing.Contracts;
using Hqqq.Api.Modules.System.Services;

namespace Hqqq.Api.Modules.Pricing.Services;

/// <summary>
/// Background service that runs a 1-second broadcast loop:
/// bootstrap -> activate pending -> compute quote -> record series -> SignalR push.
/// Series recording is throttled to <see cref="PricingOptions.SeriesRecordIntervalMs"/>
/// and gated to US market hours (9:30-16:00 ET).
/// </summary>
public sealed class QuoteBroadcastService : BackgroundService
{
    private readonly PricingEngine _engine;
    private readonly ISeriesStore _seriesStore;
    private readonly IHubContext<MarketHub> _hubContext;
    private readonly MetricsService _metrics;
    private readonly EventRecorderService _recorder;
    private readonly PricingOptions _options;
    private readonly ILogger<QuoteBroadcastService> _logger;

    private DateTimeOffset _nextRecordAt = DateTimeOffset.MinValue;
    private DateTimeOffset _nextFlushAt = DateTimeOffset.MinValue;
    private DateOnly _lastSeriesDate;

    public QuoteBroadcastService(
        PricingEngine engine,
        ISeriesStore seriesStore,
        IHubContext<MarketHub> hubContext,
        MetricsService metrics,
        EventRecorderService recorder,
        IOptions<PricingOptions> options,
        ILogger<QuoteBroadcastService> logger)
    {
        _engine = engine;
        _seriesStore = seriesStore;
        _hubContext = hubContext;
        _metrics = metrics;
        _recorder = recorder;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Quote broadcast service starting");

        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        await InitializeSeriesAsync(stoppingToken);

        var interval = TimeSpan.FromMilliseconds(_options.QuoteBroadcastIntervalMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_engine.IsInitialized)
                {
                    var bootstrapped = await _engine.TryBootstrapAsync(stoppingToken);
                    if (bootstrapped)
                        _logger.LogInformation("Pricing engine bootstrapped successfully");
                }
                else
                {
                    await _engine.TryActivatePendingAsync(stoppingToken);
                }

                var quote = _engine.ComputeQuote();
                if (quote is not null)
                {
                    MaybeRecordSeriesPoint(quote);

                    var broadcastSw = Stopwatch.StartNew();
                    await _hubContext.Clients.All
                        .SendAsync("QuoteUpdate", quote, stoppingToken);
                    broadcastSw.Stop();

                    _metrics.RecordQuoteBroadcast(broadcastSw.Elapsed.TotalMilliseconds);
                    _metrics.IncrementQuoteBroadcasts();

                    _metrics.SetSnapshotAge(
                        (DateTimeOffset.UtcNow - quote.AsOf).TotalMilliseconds);
                    _metrics.SetPricedWeightCoverage(_engine.GetPricedWeightCoverage());

                    var staleRatio = quote.Freshness.SymbolsTotal > 0
                        ? (double)quote.Freshness.SymbolsStale / quote.Freshness.SymbolsTotal
                        : 0;
                    _metrics.SetStaleSymbolRatio(staleRatio);

                    // Tick-to-quote: time from most recent tick ingestion to broadcast.
                    // This is a lower bound for the latest tick; for ticks earlier in the
                    // same cycle the true latency is up to one broadcast interval longer.
                    double? tickToQuoteMsValue = null;
                    if (quote.Freshness.LastTickUtc is not null)
                    {
                        var tickToQuoteMs = (DateTimeOffset.UtcNow - quote.Freshness.LastTickUtc.Value)
                            .TotalMilliseconds;
                        if (tickToQuoteMs >= 0)
                        {
                            _metrics.RecordTickToQuote(tickToQuoteMs);
                            tickToQuoteMsValue = tickToQuoteMs;
                        }
                    }

                    _recorder.RecordQuote(
                        quote.Nav,
                        quote.MarketPrice,
                        Math.Round(quote.PremiumDiscountPct * 100m, 2),
                        quote.Freshness.SymbolsTotal,
                        quote.Freshness.SymbolsStale,
                        broadcastSw.Elapsed.TotalMilliseconds,
                        tickToQuoteMsValue);
                }

                await MaybeFlushSeriesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Quote broadcast cycle error");
            }

            await Task.Delay(interval, stoppingToken);
        }

        await FlushSeriesAsync(stoppingToken);
        _logger.LogInformation("Quote broadcast service stopped");
    }

    private async Task InitializeSeriesAsync(CancellationToken ct)
    {
        await _engine.InitializeAsync(ct);

        var persisted = await _seriesStore.LoadAsync(ct);
        if (persisted.Count > 0)
        {
            var today = GetMarketDate();
            var latestPoint = persisted[^1];
            var pointDate = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(latestPoint.Time, _engine.MarketTimeZone).DateTime);

            if (pointDate == today)
            {
                _engine.LoadSeries(persisted);
                _lastSeriesDate = today;
                _logger.LogInformation(
                    "Restored {Count} series points for today ({Date})",
                    persisted.Count, today);
            }
            else
            {
                _logger.LogInformation(
                    "Persisted series date {PersistedDate} != today {Today}; starting fresh",
                    pointDate, today);
            }
        }
    }

    private void MaybeRecordSeriesPoint(Contracts.QuoteSnapshot quote)
    {
        if (!_engine.IsWithinMarketHours()) return;

        var now = DateTimeOffset.UtcNow;
        var today = GetMarketDate();

        if (_lastSeriesDate != default && _lastSeriesDate != today)
        {
            _engine.ClearSeries();
            _logger.LogInformation("New trading day {Date} — series buffer cleared", today);
        }
        _lastSeriesDate = today;

        if (now < _nextRecordAt) return;

        _engine.RecordSeriesPoint(quote);
        _nextRecordAt = now.AddMilliseconds(_options.SeriesRecordIntervalMs);
    }

    private async Task MaybeFlushSeriesAsync(CancellationToken ct)
    {
        if (!_engine.IsWithinMarketHours()) return;

        var now = DateTimeOffset.UtcNow;
        if (now < _nextFlushAt) return;

        await FlushSeriesAsync(ct);
        _nextFlushAt = now.AddSeconds(60);
    }

    private async Task FlushSeriesAsync(CancellationToken ct)
    {
        var series = _engine.GetSeries();
        if (series.Count == 0) return;

        await _seriesStore.SaveAsync(series, ct);
    }

    private DateOnly GetMarketDate()
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _engine.MarketTimeZone);
        return DateOnly.FromDateTime(local);
    }
}
