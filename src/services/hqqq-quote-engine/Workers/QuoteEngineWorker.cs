using Hqqq.QuoteEngine.Abstractions;
using Hqqq.QuoteEngine.Models;
using Hqqq.QuoteEngine.Services;
using Hqqq.QuoteEngine.State;

namespace Hqqq.QuoteEngine.Workers;


/// <summary>
/// Hosted worker orchestrating the engine pipeline once upstream consumers
/// are pumping into the in-process sinks:
/// <list type="number">
///   <item>drain basket activations → <see cref="IQuoteEngine.OnBasketActivated"/> (+ checkpoint write)</item>
///   <item>drain normalized ticks → <see cref="IQuoteEngine.OnTick"/></item>
///   <item>materialize snapshot + delta on a cadence, write latest Redis state and publish a Kafka snapshot event</item>
///   <item>persist a lightweight checkpoint periodically so a restart reinstalls the active basket</item>
/// </list>
/// </summary>
public sealed class QuoteEngineWorker : BackgroundService
{
    private readonly IQuoteEngine _engine;
    private readonly IRawTickFeed _tickFeed;
    private readonly IBasketStateFeed _basketFeed;
    private readonly QuoteEngineOptions _options;
    private readonly IEngineCheckpointStore _checkpointStore;
    private readonly IQuoteSnapshotSink _quoteSink;
    private readonly IConstituentSnapshotSink _constituentsSink;
    private readonly ISnapshotEventPublisher _eventPublisher;
    private readonly ConstituentsSnapshotMaterializer _constituentsMaterializer;
    private readonly QuoteSnapshotV1Mapper _snapshotEventMapper;
    private readonly ILogger<QuoteEngineWorker> _logger;

    private ActiveBasket? _lastActiveBasket;
    private SnapshotDigest? _lastSnapshotDigest;
    private string? _lastCheckpointedFingerprint;
    private DateTimeOffset? _lastCheckpointedComputedAt;

    public QuoteEngineWorker(
        IQuoteEngine engine,
        IRawTickFeed tickFeed,
        IBasketStateFeed basketFeed,
        QuoteEngineOptions options,
        IEngineCheckpointStore checkpointStore,
        IQuoteSnapshotSink quoteSink,
        IConstituentSnapshotSink constituentsSink,
        ISnapshotEventPublisher eventPublisher,
        ConstituentsSnapshotMaterializer constituentsMaterializer,
        QuoteSnapshotV1Mapper snapshotEventMapper,
        ILogger<QuoteEngineWorker> logger)
    {
        _engine = engine;
        _tickFeed = tickFeed;
        _basketFeed = basketFeed;
        _options = options;
        _checkpointStore = checkpointStore;
        _quoteSink = quoteSink;
        _constituentsSink = constituentsSink;
        _eventPublisher = eventPublisher;
        _constituentsMaterializer = constituentsMaterializer;
        _snapshotEventMapper = snapshotEventMapper;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "QuoteEngineWorker starting (staleAfter={StaleSec}s, seriesInterval={SeriesMs}ms, anchor={Anchor}, checkpointEvery={CheckpointSec}s, checkpointPath={Path})",
            _options.StaleAfter.TotalSeconds,
            _options.SeriesRecordInterval.TotalMilliseconds,
            _options.AnchorSymbol,
            _options.CheckpointInterval.TotalSeconds,
            _options.CheckpointPath);

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

        _logger.LogInformation("QuoteEngineWorker stopping");
    }

    private async Task PumpBasketAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var basket in _basketFeed.ConsumeAsync(ct).ConfigureAwait(false))
            {
                _engine.OnBasketActivated(basket);
                _lastActiveBasket = basket;

                _logger.LogInformation(
                    "Basket activated: {BasketId} fp={Fingerprint} constituents={Count} scale={Scale:E4}",
                    basket.BasketId,
                    basket.Fingerprint,
                    basket.Constituents.Count,
                    basket.ScaleFactor.Value);

                await TryWriteCheckpointAsync(basket, _lastSnapshotDigest, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Basket feed pump terminated unexpectedly");
            throw;
        }
    }

    private async Task PumpTicksAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var tick in _tickFeed.ConsumeAsync(ct).ConfigureAwait(false))
            {
                _engine.OnTick(tick);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tick feed pump terminated unexpectedly");
            throw;
        }
    }

    private async Task MaterializeLoopAsync(CancellationToken ct)
    {
        // Match the legacy QuoteBroadcastService cadence (1 Hz by default)
        // so the B4 cut-over lines up with what the current frontend expects.
        var interval = _options.MaterializeInterval;
        var nextCheckpointAt = DateTimeOffset.UtcNow + _options.CheckpointInterval;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (_engine.IsInitialized)
                {
                    var snapshot = _engine.BuildSnapshot();
                    var delta = _engine.BuildDelta();

                    if (snapshot is not null && delta is not null && _lastActiveBasket is { } basket)
                    {
                        _logger.LogDebug(
                            "Materialized snapshot nav={Nav} qqq={Qqq} premDisc={Pct}% fresh={Fresh}/{Total}",
                            snapshot.Nav, snapshot.Qqq, snapshot.PremiumDiscountPct,
                            snapshot.Freshness.SymbolsFresh, snapshot.Freshness.SymbolsTotal);

                        await PublishOutputsAsync(basket, snapshot, ct).ConfigureAwait(false);

                        _lastSnapshotDigest = new SnapshotDigest
                        {
                            Nav = snapshot.Nav,
                            Qqq = snapshot.Qqq,
                            PremiumDiscountPct = snapshot.PremiumDiscountPct,
                            ComputedAtUtc = snapshot.AsOf,
                        };
                    }

                    if (DateTimeOffset.UtcNow >= nextCheckpointAt)
                    {
                        if (_lastActiveBasket is { } checkpointBasket)
                            await TryWriteCheckpointAsync(checkpointBasket, _lastSnapshotDigest, ct).ConfigureAwait(false);

                        nextCheckpointAt = DateTimeOffset.UtcNow + _options.CheckpointInterval;
                    }
                }

                await Task.Delay(interval, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private async Task PublishOutputsAsync(
        ActiveBasket basket, Hqqq.Contracts.Dtos.QuoteSnapshotDto snapshot, CancellationToken ct)
    {
        // Each sink is isolated: one failure must not block the other two,
        // and must never kill the worker. The engine is the authoritative
        // owner of state — Redis / Kafka are just projections of it.
        try
        {
            await _quoteSink.WriteAsync(basket.BasketId, snapshot, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Redis snapshot write failed for basket {BasketId}", basket.BasketId);
        }

        try
        {
            var constituentsDto = _constituentsMaterializer.Build();
            if (constituentsDto is not null)
            {
                await _constituentsSink
                    .WriteAsync(basket.BasketId, constituentsDto, ct)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Redis constituents write failed for basket {BasketId}", basket.BasketId);
        }

        try
        {
            var evt = _snapshotEventMapper.Map(basket.BasketId, snapshot);
            await _eventPublisher.PublishAsync(evt, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Kafka snapshot publish failed for basket {BasketId}", basket.BasketId);
        }
    }

    private async Task TryWriteCheckpointAsync(
        ActiveBasket basket, SnapshotDigest? digest, CancellationToken ct)
    {
        // Skip redundant writes when neither basket fingerprint nor snapshot
        // computed-at has advanced — the periodic cadence can otherwise thrash
        // disk on an idle engine.
        if (string.Equals(_lastCheckpointedFingerprint, basket.Fingerprint, StringComparison.Ordinal)
            && _lastCheckpointedComputedAt == digest?.ComputedAtUtc)
        {
            return;
        }

        try
        {
            var checkpoint = new EngineCheckpoint
            {
                WrittenAtUtc = DateTimeOffset.UtcNow,
                Basket = ActiveBasketMapper.ToEvent(basket),
                LastSnapshot = digest,
            };
            await _checkpointStore.SaveAsync(checkpoint, ct).ConfigureAwait(false);

            _lastCheckpointedFingerprint = basket.Fingerprint;
            _lastCheckpointedComputedAt = digest?.ComputedAtUtc;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Checkpoint write failed for basket fp={Fingerprint}", basket.Fingerprint);
        }
    }
}
