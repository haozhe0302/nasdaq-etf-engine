using Hqqq.Contracts.Events;
using Hqqq.Ingress.Clients;
using Hqqq.Ingress.Configuration;
using Hqqq.Ingress.Publishing;
using Hqqq.Ingress.State;
using Hqqq.Ingress.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hqqq.Ingress.Tests;

/// <summary>
/// StartAsync gates the worker on the Tiingo API key. Phase 2 ingress has a
/// single self-sufficient runtime path — these tests assert the
/// operator-visible fail-fast behaviour and the basket-wait / bootstrap
/// override semantics without needing a real websocket or Kafka.
/// </summary>
public class TiingoIngressWorkerStartupTests
{
    [Fact]
    public async Task MissingApiKey_FailsFast()
    {
        var worker = BuildWorker(apiKey: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => worker.StartAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("<set tiingo api key>")]
    [InlineData("YOUR_API_KEY")]
    [InlineData("changeme")]
    [InlineData("REPLACE_ME_TO_RUN")]
    [InlineData("   ")]
    public async Task PlaceholderApiKey_FailsFast(string placeholder)
    {
        var worker = BuildWorker(apiKey: placeholder);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => worker.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RealApiKey_StartsCleanly()
    {
        var worker = BuildWorker(apiKey: "real-looking-key");

        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task WhenBasketArrives_WorkerSubscribesThroughCoordinator()
    {
        var universe = new ActiveSymbolUniverse();
        var fakeClient = new FakeTiingoStreamClient();
        var coordinator = new BasketSubscriptionCoordinator(
            universe, fakeClient, NullLogger<BasketSubscriptionCoordinator>.Instance);

        var worker = BuildWorker(
            apiKey: "real-key",
            universe: universe,
            coordinator: coordinator,
            streamClient: fakeClient,
            basketOptions: new IngressBasketOptions { StartupWaitSeconds = 5 });

        using var cts = new CancellationTokenSource();

        // Deliver a basket on a background thread shortly after start.
        _ = Task.Run(async () =>
        {
            await Task.Delay(100);
            universe.SetFromBasket(
                "HQQQ", "fp-1", new DateOnly(2026, 4, 18),
                new[] { "AAPL", "MSFT" }, "test",
                DateTimeOffset.UtcNow);
        });

        await worker.StartAsync(cts.Token);
        // Give ExecuteAsync enough time to drain the basket + attempt connect.
        await Task.Delay(500);
        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(new[] { "AAPL", "MSFT" }, coordinator.CurrentAppliedSymbols.OrderBy(s => s).ToArray());
        Assert.Equal("fp-1", coordinator.AppliedFingerprint);
    }

    [Fact]
    public async Task WhenNoBasketAndOverrideConfigured_UsesBootstrapOverride()
    {
        var universe = new ActiveSymbolUniverse();
        var fakeClient = new FakeTiingoStreamClient();
        var coordinator = new BasketSubscriptionCoordinator(
            universe, fakeClient, NullLogger<BasketSubscriptionCoordinator>.Instance);

        var worker = BuildWorker(
            apiKey: "real-key",
            universe: universe,
            coordinator: coordinator,
            streamClient: fakeClient,
            tiingoOptions: new TiingoOptions
            {
                ApiKey = "real-key",
                Symbols = "aapl,msft",
                SnapshotOnStartup = false,
            },
            basketOptions: new IngressBasketOptions { StartupWaitSeconds = 1 });

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await Task.Delay(1500); // wait past StartupWaitSeconds
        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(new[] { "AAPL", "MSFT" }, coordinator.CurrentAppliedSymbols.OrderBy(s => s).ToArray());
        Assert.Equal("bootstrap:override", coordinator.AppliedFingerprint);
    }

    private static TiingoIngressWorker BuildWorker(
        string? apiKey,
        ActiveSymbolUniverse? universe = null,
        BasketSubscriptionCoordinator? coordinator = null,
        ITiingoStreamClient? streamClient = null,
        ITiingoSnapshotClient? snapshotClient = null,
        ITickPublisher? publisher = null,
        TiingoOptions? tiingoOptions = null,
        IngressBasketOptions? basketOptions = null)
    {
        universe ??= new ActiveSymbolUniverse();
        streamClient ??= new FakeTiingoStreamClient();
        coordinator ??= new BasketSubscriptionCoordinator(
            universe, streamClient, NullLogger<BasketSubscriptionCoordinator>.Instance);
        snapshotClient ??= new NoOpSnapshotClient();
        publisher ??= new NoOpTickPublisher();
        tiingoOptions ??= new TiingoOptions { ApiKey = apiKey, SnapshotOnStartup = false };
        if (tiingoOptions.ApiKey is null && apiKey is not null)
        {
            tiingoOptions = new TiingoOptions
            {
                ApiKey = apiKey,
                Symbols = tiingoOptions.Symbols,
                SnapshotOnStartup = tiingoOptions.SnapshotOnStartup,
            };
        }
        basketOptions ??= new IngressBasketOptions { StartupWaitSeconds = 0 };

        return new TiingoIngressWorker(
            streamClient: streamClient,
            snapshotClient: snapshotClient,
            publisher: publisher,
            state: new IngestionState(),
            universe: universe,
            coordinator: coordinator,
            tiingoOptions: Options.Create(tiingoOptions),
            basketOptions: Options.Create(basketOptions),
            logger: NullLogger<TiingoIngressWorker>.Instance);
    }

    internal sealed class FakeTiingoStreamClient : ITiingoStreamClient
    {
        public bool IsConnected { get; private set; }
        public DateTimeOffset? LastDataUtc => null;
        public List<string[]> Subscribes { get; } = new();
        public List<string[]> Unsubscribes { get; } = new();

        public async Task ConnectAndStreamAsync(
            IEnumerable<string> symbols,
            Func<RawTickV1, CancellationToken, Task> onTick,
            CancellationToken ct)
        {
            IsConnected = true;
            try
            {
                await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                IsConnected = false;
            }
        }

        public Task SubscribeAsync(IEnumerable<string> symbols, CancellationToken ct)
        {
            Subscribes.Add(symbols.ToArray());
            return Task.CompletedTask;
        }

        public Task UnsubscribeAsync(IEnumerable<string> symbols, CancellationToken ct)
        {
            Unsubscribes.Add(symbols.ToArray());
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpSnapshotClient : ITiingoSnapshotClient
    {
        public Task<IReadOnlyList<RawTickV1>> FetchSnapshotsAsync(
            IEnumerable<string> symbols, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<RawTickV1>>(Array.Empty<RawTickV1>());
    }

    private sealed class NoOpTickPublisher : ITickPublisher
    {
        public Task PublishAsync(RawTickV1 tick, CancellationToken ct) => Task.CompletedTask;
        public Task PublishBatchAsync(IEnumerable<RawTickV1> ticks, CancellationToken ct) => Task.CompletedTask;
    }
}
