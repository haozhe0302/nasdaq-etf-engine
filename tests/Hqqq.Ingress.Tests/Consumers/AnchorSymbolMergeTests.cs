using Hqqq.Contracts.Events;
using Hqqq.Infrastructure.Kafka;
using Hqqq.Ingress.Clients;
using Hqqq.Ingress.Configuration;
using Hqqq.Ingress.Consumers;
using Hqqq.Ingress.Publishing;
using Hqqq.Ingress.State;
using Hqqq.Ingress.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hqqq.Ingress.Tests.Consumers;

/// <summary>
/// Covers <see cref="IngressBasketOptions.AnchorSymbols"/> merge semantics
/// across the two seams that feed the Tiingo subscription set:
/// <list type="bullet">
///   <item><description><see cref="BasketActiveConsumer.Apply"/> — basket-driven path.</description></item>
///   <item><description><see cref="TiingoIngressWorker"/> bootstrap-override path — used when no basket arrives within <c>StartupWaitSeconds</c>.</description></item>
/// </list>
/// The anchor symbol is the QQQ ticker; without it, the ingress→quote-engine
/// pipeline cannot expose <c>marketPrice</c>/<c>qqq</c> on <c>/api/quote</c>
/// or bootstrap the iNAV scale factor.
/// </summary>
public class AnchorSymbolMergeTests
{
    [Fact]
    public void Apply_BasketWithAnchorQQQ_MergesIntoUniverseAndCoordinator()
    {
        var (consumer, universe, coordinator, client) = BuildConsumer(anchors: new[] { "QQQ" });

        var basket = NewBasket("HQQQ", "fp-1", new[] { "AAPL", "MSFT" });

        Assert.True(consumer.Apply(basket));

        Assert.NotNull(universe.Current);
        Assert.Equal(
            new[] { "AAPL", "MSFT", "QQQ" },
            universe.Current!.Symbols.OrderBy(s => s).ToArray());

        SpinUntil(() => client.Subscribes.Count > 0);
        Assert.Single(client.Subscribes);
        Assert.Equal(
            new[] { "AAPL", "MSFT", "QQQ" },
            client.Subscribes[0].OrderBy(s => s).ToArray());
        Assert.Equal("fp-1", coordinator.AppliedFingerprint);
    }

    [Fact]
    public void Apply_BasketContainsAnchorSymbol_DoesNotDuplicate()
    {
        var (consumer, universe, _, client) = BuildConsumer(anchors: new[] { "QQQ" });

        var basket = NewBasket("HQQQ", "fp-1", new[] { "AAPL", "QQQ" });

        Assert.True(consumer.Apply(basket));

        Assert.Equal(
            new[] { "AAPL", "QQQ" },
            universe.Current!.Symbols.OrderBy(s => s).ToArray());

        SpinUntil(() => client.Subscribes.Count > 0);
        Assert.Equal(
            new[] { "AAPL", "QQQ" },
            client.Subscribes[0].OrderBy(s => s).ToArray());
    }

    [Fact]
    public void Apply_AnchorPersistsAcrossFingerprintChange()
    {
        var (consumer, _, coordinator, client) = BuildConsumer(anchors: new[] { "QQQ" });

        var v1 = NewBasket("HQQQ", "fp-1", new[] { "AAPL", "MSFT", "NVDA" });
        var v2 = NewBasket("HQQQ", "fp-2", new[] { "AAPL", "GOOG" });

        Assert.True(consumer.Apply(v1));
        SpinUntil(() => client.Subscribes.Count >= 1);
        Assert.True(consumer.Apply(v2));
        SpinUntil(() => client.Subscribes.Count >= 2);

        // First apply: subscribe to entire v1 union (constituents + anchor).
        Assert.Equal(
            new[] { "AAPL", "MSFT", "NVDA", "QQQ" },
            client.Subscribes[0].OrderBy(s => s).ToArray());

        // Second apply: GOOG added, MSFT+NVDA dropped, QQQ untouched.
        Assert.Equal(new[] { "GOOG" }, client.Subscribes[1].OrderBy(s => s).ToArray());
        Assert.Equal(
            new[] { "MSFT", "NVDA" },
            client.Unsubscribes[0].OrderBy(s => s).ToArray());
        Assert.Equal("fp-2", coordinator.AppliedFingerprint);
        Assert.Contains("QQQ", coordinator.CurrentAppliedSymbols);
    }

    [Fact]
    public void Apply_AnchorsCaseInsensitive_DeduplicatedAgainstConstituents()
    {
        var (consumer, universe, _, _) = BuildConsumer(anchors: new[] { "qqq", "QQQ", " spy " });

        var basket = NewBasket("HQQQ", "fp-1", new[] { "AAPL", "Qqq" });

        Assert.True(consumer.Apply(basket));

        Assert.Equal(
            new[] { "AAPL", "QQQ", "SPY" },
            universe.Current!.Symbols.OrderBy(s => s).ToArray());
    }

    [Fact]
    public void Apply_NoAnchors_BehavesLikeLegacyBasket()
    {
        var (consumer, universe, _, _) = BuildConsumer(anchors: Array.Empty<string>());

        var basket = NewBasket("HQQQ", "fp-1", new[] { "AAPL", "MSFT" });

        Assert.True(consumer.Apply(basket));

        Assert.Equal(
            new[] { "AAPL", "MSFT" },
            universe.Current!.Symbols.OrderBy(s => s).ToArray());
    }

    [Fact]
    public void Apply_EmptyConstituentsStillRejected_AnchorsDoNotMaskBadBasket()
    {
        var (consumer, universe, _, client) = BuildConsumer(anchors: new[] { "QQQ" });

        var bad = NewBasket("HQQQ", "fp-1", Array.Empty<string>());

        Assert.False(consumer.Apply(bad));
        Assert.Null(universe.Current);
        Assert.Empty(client.Subscribes);
    }

    [Fact]
    public async Task BootstrapOverride_NoBasket_AnchorsMergedWithOverride()
    {
        var universe = new ActiveSymbolUniverse();
        var fakeClient = new FakeTiingoStreamClient();
        var coordinator = new BasketSubscriptionCoordinator(
            universe, fakeClient, NullLogger<BasketSubscriptionCoordinator>.Instance);

        var worker = new TiingoIngressWorker(
            streamClient: fakeClient,
            snapshotClient: new NoOpSnapshotClient(),
            publisher: new NoOpTickPublisher(),
            state: new IngestionState(),
            universe: universe,
            coordinator: coordinator,
            tiingoOptions: Options.Create(new TiingoOptions
            {
                ApiKey = "real-key",
                Symbols = "aapl,msft",
                SnapshotOnStartup = false,
            }),
            basketOptions: Options.Create(new IngressBasketOptions
            {
                StartupWaitSeconds = 1,
                AnchorSymbols = new[] { "QQQ" },
            }),
            logger: NullLogger<TiingoIngressWorker>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await Task.Delay(1500);
        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(
            new[] { "AAPL", "MSFT", "QQQ" },
            coordinator.CurrentAppliedSymbols.OrderBy(s => s).ToArray());
        Assert.Equal("bootstrap:override", coordinator.AppliedFingerprint);
    }

    [Fact]
    public async Task BootstrapOverride_NoBasketNoOverride_AnchorsAloneSeedSubscription()
    {
        var universe = new ActiveSymbolUniverse();
        var fakeClient = new FakeTiingoStreamClient();
        var coordinator = new BasketSubscriptionCoordinator(
            universe, fakeClient, NullLogger<BasketSubscriptionCoordinator>.Instance);

        var worker = new TiingoIngressWorker(
            streamClient: fakeClient,
            snapshotClient: new NoOpSnapshotClient(),
            publisher: new NoOpTickPublisher(),
            state: new IngestionState(),
            universe: universe,
            coordinator: coordinator,
            tiingoOptions: Options.Create(new TiingoOptions
            {
                ApiKey = "real-key",
                Symbols = string.Empty,
                SnapshotOnStartup = false,
            }),
            basketOptions: Options.Create(new IngressBasketOptions
            {
                StartupWaitSeconds = 1,
                AnchorSymbols = new[] { "QQQ" },
            }),
            logger: NullLogger<TiingoIngressWorker>.Instance);

        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        await Task.Delay(1500);
        await cts.CancelAsync();
        await worker.StopAsync(CancellationToken.None);

        Assert.Equal(new[] { "QQQ" }, coordinator.CurrentAppliedSymbols.ToArray());
        Assert.Equal("bootstrap:override", coordinator.AppliedFingerprint);
    }

    [Fact]
    public void ResolveAnchorSymbols_NormalisesTrimUpperDedupe_DropsBlanks()
    {
        var options = new IngressBasketOptions
        {
            AnchorSymbols = new[] { " qqq ", "QQQ", "", "  ", "spy" },
        };

        var resolved = options.ResolveAnchorSymbols();

        Assert.Equal(new[] { "QQQ", "SPY" }, resolved.OrderBy(s => s).ToArray());
    }

    [Fact]
    public void ResolveAnchorSymbols_NullOrEmptyConfiguration_ReturnsEmpty()
    {
        Assert.Empty(new IngressBasketOptions { AnchorSymbols = Array.Empty<string>() }.ResolveAnchorSymbols());
        Assert.Empty(new IngressBasketOptions { AnchorSymbols = null! }.ResolveAnchorSymbols());
    }

    [Fact]
    public void DefaultAnchorSymbols_IncludeQQQ()
    {
        // Safety-by-default: a freshly bound IngressBasketOptions (no IaC
        // override) must already subscribe to QQQ.
        var options = new IngressBasketOptions();
        Assert.Equal(new[] { "QQQ" }, options.ResolveAnchorSymbols().ToArray());
    }

    private static (BasketActiveConsumer Consumer,
                    ActiveSymbolUniverse Universe,
                    BasketSubscriptionCoordinator Coordinator,
                    RecordingClient Client) BuildConsumer(string[] anchors)
    {
        var universe = new ActiveSymbolUniverse();
        var client = new RecordingClient();
        var coordinator = new BasketSubscriptionCoordinator(
            universe, client, NullLogger<BasketSubscriptionCoordinator>.Instance);

        var consumer = new BasketActiveConsumer(
            universe,
            Options.Create(new KafkaOptions()),
            Options.Create(new IngressBasketOptions { AnchorSymbols = anchors }),
            NullLogger<BasketActiveConsumer>.Instance);

        return (consumer, universe, coordinator, client);
    }

    private static BasketActiveStateV1 NewBasket(
        string basketId, string fingerprint, IEnumerable<string> symbols)
    {
        var constituents = symbols
            .Select(s => new BasketConstituentV1
            {
                Symbol = s,
                SecurityName = $"{s} Corp",
                Sector = "Technology",
                SharesHeld = 100m,
                SharesOrigin = "test",
            })
            .ToArray();

        var entries = constituents
            .Where(c => !string.IsNullOrWhiteSpace(c.Symbol))
            .Select(c => new PricingBasisEntryV1
            {
                Symbol = c.Symbol,
                Shares = 100,
                ReferencePrice = 10m,
                SharesOrigin = "test",
            })
            .ToArray();

        return new BasketActiveStateV1
        {
            BasketId = basketId,
            Fingerprint = fingerprint,
            Version = "v-test",
            AsOfDate = new DateOnly(2026, 4, 18),
            ActivatedAtUtc = DateTimeOffset.UtcNow,
            Constituents = constituents,
            PricingBasis = new PricingBasisV1
            {
                PricingBasisFingerprint = fingerprint,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Entries = entries,
                InferredTotalNotional = Math.Max(1, entries.Length) * 10m,
                OfficialSharesCount = entries.Length,
                DerivedSharesCount = 0,
            },
            ScaleFactor = 1m,
            Source = "test",
            ConstituentCount = constituents.Length,
        };
    }

    private static void SpinUntil(Func<bool> predicate, int maxMs = 500)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(maxMs);
        while (DateTime.UtcNow < deadline && !predicate())
        {
            Thread.Sleep(10);
        }
    }

    private sealed class RecordingClient : ITiingoStreamClient
    {
        public bool IsConnected => false;
        public DateTimeOffset? LastDataUtc => null;
        public List<string[]> Subscribes { get; } = new();
        public List<string[]> Unsubscribes { get; } = new();

        public Task ConnectAndStreamAsync(
            IEnumerable<string> symbols,
            Func<RawTickV1, CancellationToken, Task> onTick,
            CancellationToken ct) => Task.CompletedTask;

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

    private sealed class FakeTiingoStreamClient : ITiingoStreamClient
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
