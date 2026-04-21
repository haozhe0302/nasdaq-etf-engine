using Hqqq.Contracts.Events;
using Hqqq.Infrastructure.Kafka;
using Hqqq.Ingress.Clients;
using Hqqq.Ingress.Configuration;
using Hqqq.Ingress.Consumers;
using Hqqq.Ingress.State;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hqqq.Ingress.Tests.Consumers;

/// <summary>
/// Direct coverage for <see cref="BasketActiveConsumer"/>. The Kafka loop
/// itself is not exercised here (no broker stand-up); instead we drive the
/// internal <c>Apply</c> projection that the loop dispatches into,
/// asserting the basket → universe → coordinator wiring and the
/// malformed-message rejections that keep ingress from acting on bad
/// upstream payloads.
/// </summary>
public class BasketActiveConsumerTests
{
    [Fact]
    public void Apply_ValidBasket_UpdatesUniverseAndTriggersCoordinator()
    {
        var (consumer, universe, coordinator, client) = BuildConsumer();

        var basket = NewBasket(
            basketId: "HQQQ",
            fingerprint: "fp-1",
            symbols: new[] { "AAPL", "MSFT", "NVDA" });

        var applied = consumer.Apply(basket);

        Assert.True(applied);
        Assert.NotNull(universe.Current);
        Assert.Equal("HQQQ", universe.Current!.BasketId);
        Assert.Equal("fp-1", universe.Current.Fingerprint);
        Assert.Equal(
            new[] { "AAPL", "MSFT", "NVDA" },
            universe.Current.Symbols.OrderBy(s => s).ToArray());

        // The coordinator subscribes via the BasketUpdated event handler;
        // wait briefly for the async invocation.
        SpinUntil(() => client.Subscribes.Count > 0);

        Assert.Single(client.Subscribes);
        Assert.Equal(
            new[] { "AAPL", "MSFT", "NVDA" },
            client.Subscribes[0].OrderBy(s => s).ToArray());
        Assert.Equal("fp-1", coordinator.AppliedFingerprint);
    }

    [Fact]
    public void Apply_TombstoneOrNullValue_IsIgnored()
    {
        var (consumer, universe, _, _) = BuildConsumer();

        var applied = consumer.Apply(null);

        Assert.False(applied);
        Assert.Null(universe.Current);
    }

    [Fact]
    public void Apply_MissingBasketIdOrFingerprint_IsRejected()
    {
        var (consumer, universe, _, client) = BuildConsumer();

        // Missing basketId.
        var bad1 = NewBasket(basketId: "", fingerprint: "fp-1", symbols: new[] { "AAPL" });
        Assert.False(consumer.Apply(bad1));

        // Missing fingerprint.
        var bad2 = NewBasket(basketId: "HQQQ", fingerprint: "", symbols: new[] { "AAPL" });
        Assert.False(consumer.Apply(bad2));

        Assert.Null(universe.Current);
        Assert.Empty(client.Subscribes);
    }

    [Fact]
    public void Apply_EmptyConstituents_IsRejected()
    {
        var (consumer, universe, _, client) = BuildConsumer();

        var bad = NewBasket(basketId: "HQQQ", fingerprint: "fp-x", symbols: Array.Empty<string>());

        Assert.False(consumer.Apply(bad));
        Assert.Null(universe.Current);
        Assert.Empty(client.Subscribes);
    }

    [Fact]
    public void Apply_EveryConstituentHasBlankSymbol_IsRejected()
    {
        var (consumer, universe, _, client) = BuildConsumer();

        var bad = NewBasket(basketId: "HQQQ", fingerprint: "fp-x", symbols: new[] { " ", "" });

        Assert.False(consumer.Apply(bad));
        Assert.Null(universe.Current);
        Assert.Empty(client.Subscribes);
    }

    [Fact]
    public void Apply_FingerprintChange_DiffsAddAndRemoveOnCoordinator()
    {
        var (consumer, _, coordinator, client) = BuildConsumer();

        var v1 = NewBasket("HQQQ", "fp-1", new[] { "AAPL", "MSFT", "NVDA" });
        var v2 = NewBasket("HQQQ", "fp-2", new[] { "AAPL", "GOOG" });

        Assert.True(consumer.Apply(v1));
        SpinUntil(() => client.Subscribes.Count >= 1);

        Assert.True(consumer.Apply(v2));
        SpinUntil(() => client.Subscribes.Count >= 2);

        Assert.Equal(2, client.Subscribes.Count);
        Assert.Equal(new[] { "GOOG" }, client.Subscribes[1]);
        Assert.Single(client.Unsubscribes);
        Assert.Equal(
            new[] { "MSFT", "NVDA" },
            client.Unsubscribes[0].OrderBy(s => s).ToArray());
        Assert.Equal("fp-2", coordinator.AppliedFingerprint);
    }

    [Fact]
    public void Apply_SameFingerprintTwice_DoesNotReSubscribe()
    {
        var (consumer, _, _, client) = BuildConsumer();

        var basket = NewBasket("HQQQ", "fp-1", new[] { "AAPL" });

        Assert.True(consumer.Apply(basket));
        Assert.True(consumer.Apply(basket));
        SpinUntil(() => client.Subscribes.Count >= 1);

        Assert.Single(client.Subscribes);
        Assert.Empty(client.Unsubscribes);
    }

    private static (BasketActiveConsumer Consumer,
                    ActiveSymbolUniverse Universe,
                    BasketSubscriptionCoordinator Coordinator,
                    RecordingClient Client) BuildConsumer()
    {
        var universe = new ActiveSymbolUniverse();
        var client = new RecordingClient();
        var coordinator = new BasketSubscriptionCoordinator(
            universe, client, NullLogger<BasketSubscriptionCoordinator>.Instance);

        var consumer = new BasketActiveConsumer(
            universe,
            Options.Create(new KafkaOptions()),
            Options.Create(new IngressBasketOptions()),
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
                InferredTotalNotional = entries.Length * 10m,
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
}
