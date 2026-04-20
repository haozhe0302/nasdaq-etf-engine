using Hqqq.ReferenceData.Publishing;
using Hqqq.ReferenceData.Repositories;
using Hqqq.ReferenceData.Standalone;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Jobs;

/// <summary>
/// Standalone-mode background job that publishes the deterministic
/// basket seed to <c>refdata.basket.active.v1</c> once on startup, then
/// re-publishes on a slow cadence (default 5 min). The compacted topic
/// makes a single publish semantically sufficient; the slow republish
/// just keeps late or restarted consumers warm without operator action.
/// </summary>
/// <remarks>
/// Publish failures are logged and retried — they do <em>not</em> crash
/// the host, because reference-data's REST surface remains useful even
/// when the broker is briefly unavailable, and the gateway / quote-engine
/// will recover as soon as the next republish succeeds.
/// </remarks>
public sealed class StandalonePublishJob : BackgroundService
{
    private readonly SeedFileBasketRepository _repository;
    private readonly IBasketPublisher _publisher;
    private readonly BasketSeedOptions _options;
    private readonly ILogger<StandalonePublishJob> _logger;

    public StandalonePublishJob(
        SeedFileBasketRepository repository,
        IBasketPublisher publisher,
        IOptions<BasketSeedOptions> options,
        ILogger<StandalonePublishJob> logger)
    {
        _repository = repository;
        _publisher = publisher;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seed = _repository.Seed;
        var activatedAt = _repository.ActivatedAtUtc;
        var interval = TimeSpan.FromSeconds(Math.Max(30, _options.RepublishIntervalSeconds));

        _logger.LogInformation(
            "StandalonePublishJob starting — basketId={BasketId} fingerprint={Fingerprint} republishInterval={Interval}",
            seed.BasketId, seed.Fingerprint, interval);

        await PublishOnceAsync(seed, activatedAt, isStartup: true, stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await PublishOnceAsync(seed, activatedAt, isStartup: false, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async Task PublishOnceAsync(BasketSeed seed, DateTimeOffset activatedAt, bool isStartup, CancellationToken ct)
    {
        try
        {
            var ev = BasketSeedToEventMapper.ToEvent(seed, activatedAt);
            await _publisher.PublishAsync(ev, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Published refdata.basket.active.v1 (startup={Startup}) basketId={BasketId} fingerprint={Fingerprint} constituents={Count}",
                isStartup, ev.BasketId, ev.Fingerprint, ev.Constituents.Count);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "Failed to publish refdata.basket.active.v1 (startup={Startup}); will retry on next interval",
                isStartup);
        }
    }
}
