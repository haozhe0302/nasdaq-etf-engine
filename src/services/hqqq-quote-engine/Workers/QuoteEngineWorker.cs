using Hqqq.QuoteEngine.Abstractions;
using Hqqq.QuoteEngine.Services;

namespace Hqqq.QuoteEngine.Workers;

/// <summary>
/// Hosted worker that orchestrates the B2 engine pipeline:
/// <list type="number">
///   <item>drain basket activations → <see cref="IQuoteEngine.OnBasketActivated"/></item>
///   <item>drain normalized ticks → <see cref="IQuoteEngine.OnTick"/></item>
///   <item>on a cadence, materialize snapshot + delta (logged, not published)</item>
/// </list>
/// No downstream Kafka / Redis / SignalR wiring yet; that arrives in B3 / B4.
/// </summary>
public sealed class QuoteEngineWorker(
    IQuoteEngine engine,
    IRawTickFeed tickFeed,
    IBasketStateFeed basketFeed,
    QuoteEngineOptions options,
    ILogger<QuoteEngineWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "QuoteEngineWorker starting (staleAfter={StaleSec}s, seriesInterval={SeriesMs}ms, anchor={Anchor})",
            options.StaleAfter.TotalSeconds,
            options.SeriesRecordInterval.TotalMilliseconds,
            options.AnchorSymbol);

        var basketPump = Task.Run(() => PumpBasketAsync(stoppingToken), stoppingToken);
        var tickPump = Task.Run(() => PumpTicksAsync(stoppingToken), stoppingToken);
        var materializer = Task.Run(() => MaterializeLoopAsync(stoppingToken), stoppingToken);

        try
        {
            await Task.WhenAll(basketPump, tickPump, materializer).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        logger.LogInformation("QuoteEngineWorker stopping");
    }

    private async Task PumpBasketAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var basket in basketFeed.ConsumeAsync(ct).ConfigureAwait(false))
            {
                engine.OnBasketActivated(basket);
                logger.LogInformation(
                    "Basket activated: {BasketId} fp={Fingerprint} constituents={Count} scale={Scale:E4}",
                    basket.BasketId,
                    basket.Fingerprint,
                    basket.Constituents.Count,
                    basket.ScaleFactor.Value);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Basket feed pump terminated unexpectedly");
            throw;
        }
    }

    private async Task PumpTicksAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var tick in tickFeed.ConsumeAsync(ct).ConfigureAwait(false))
            {
                engine.OnTick(tick);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Tick feed pump terminated unexpectedly");
            throw;
        }
    }

    private async Task MaterializeLoopAsync(CancellationToken ct)
    {
        // Match the legacy QuoteBroadcastService cadence (1 Hz) so the B3
        // cut-over lines up with what the current frontend expects.
        var interval = TimeSpan.FromSeconds(1);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (engine.IsInitialized)
                {
                    var snapshot = engine.BuildSnapshot();
                    var delta = engine.BuildDelta();

                    if (snapshot is not null && delta is not null)
                    {
                        // TODO(B3): publish delta to pricing.snapshots.v1 + Redis cache.
                        // TODO(B4): fan delta out to SignalR via hqqq-gateway.
                        logger.LogDebug(
                            "Materialized snapshot nav={Nav} qqq={Qqq} premDisc={Pct}% fresh={Fresh}/{Total}",
                            snapshot.Nav, snapshot.Qqq, snapshot.PremiumDiscountPct,
                            snapshot.Freshness.SymbolsFresh, snapshot.Freshness.SymbolsTotal);
                    }
                }

                await Task.Delay(interval, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }
}
