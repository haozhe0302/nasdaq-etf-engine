using Hqqq.QuoteEngine.Abstractions;
using Hqqq.QuoteEngine.Consumers;
using Hqqq.QuoteEngine.Models;

namespace Hqqq.QuoteEngine.Persistence;

/// <summary>
/// Plain <see cref="IHostedService"/> that runs the checkpoint-restore step
/// during <see cref="StartAsync"/>, before any consumer or worker spins up.
/// Registering it ahead of the Kafka consumers guarantees the engine has a
/// pre-hydrated basket (when one exists) before the first live message is
/// handled, without racing the single-writer invariant.
/// </summary>
/// <remarks>
/// Intentionally swallows all failures: the acceptance criteria require the
/// engine to survive a missing or corrupt checkpoint.
/// </remarks>
public sealed class EngineCheckpointRestorer : IHostedService
{
    private readonly IEngineCheckpointStore _store;
    private readonly IQuoteEngine _engine;
    private readonly BasketEventConsumer _basketConsumer;
    private readonly ILogger<EngineCheckpointRestorer> _logger;

    public EngineCheckpointRestorer(
        IEngineCheckpointStore store,
        IQuoteEngine engine,
        BasketEventConsumer basketConsumer,
        ILogger<EngineCheckpointRestorer> logger)
    {
        _store = store;
        _engine = engine;
        _basketConsumer = basketConsumer;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            var checkpoint = await _store.LoadAsync(ct).ConfigureAwait(false);
            if (checkpoint is null)
            {
                _logger.LogInformation("No engine checkpoint to restore — starting cold");
                return;
            }

            var basket = ActiveBasketMapper.ToActiveBasket(checkpoint.Basket);
            _engine.OnBasketActivated(basket);
            _basketConsumer.PrimeFromRestoredFingerprint(basket.Fingerprint);

            if (checkpoint.LastSnapshot is { } snap)
            {
                _logger.LogInformation(
                    "Restored engine checkpoint: basket={BasketId} fp={Fingerprint} writtenAt={WrittenAt} lastNav={Nav} lastQqq={Qqq} computedAt={ComputedAt}",
                    basket.BasketId, basket.Fingerprint, checkpoint.WrittenAtUtc,
                    snap.Nav, snap.Qqq, snap.ComputedAtUtc);
            }
            else
            {
                _logger.LogInformation(
                    "Restored engine checkpoint: basket={BasketId} fp={Fingerprint} writtenAt={WrittenAt} (no snapshot digest)",
                    basket.BasketId, basket.Fingerprint, checkpoint.WrittenAtUtc);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Engine checkpoint restore failed — continuing cold");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
