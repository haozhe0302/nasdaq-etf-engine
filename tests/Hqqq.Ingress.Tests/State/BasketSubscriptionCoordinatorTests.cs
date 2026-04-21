using Hqqq.Contracts.Events;
using Hqqq.Ingress.Clients;
using Hqqq.Ingress.State;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hqqq.Ingress.Tests.State;

public class BasketSubscriptionCoordinatorTests
{
    [Fact]
    public async Task ApplyAsync_FirstBasket_SubscribesAll()
    {
        var universe = new ActiveSymbolUniverse();
        var client = new RecordingClient();
        using var coordinator = new BasketSubscriptionCoordinator(
            universe, client, NullLogger<BasketSubscriptionCoordinator>.Instance);

        var snap = BuildSnapshot("fp-1", new[] { "AAPL", "MSFT" });
        await coordinator.ApplyAsync(snap, CancellationToken.None);

        Assert.Single(client.Subscribes);
        Assert.Equal(new[] { "AAPL", "MSFT" }, client.Subscribes[0].OrderBy(s => s).ToArray());
        Assert.Empty(client.Unsubscribes);
        Assert.Equal("fp-1", coordinator.AppliedFingerprint);
    }

    [Fact]
    public async Task ApplyAsync_SameFingerprint_IsIdempotent()
    {
        var universe = new ActiveSymbolUniverse();
        var client = new RecordingClient();
        using var coordinator = new BasketSubscriptionCoordinator(
            universe, client, NullLogger<BasketSubscriptionCoordinator>.Instance);

        var snap = BuildSnapshot("fp-1", new[] { "AAPL" });
        await coordinator.ApplyAsync(snap, CancellationToken.None);
        await coordinator.ApplyAsync(snap, CancellationToken.None);

        Assert.Single(client.Subscribes);
        Assert.Empty(client.Unsubscribes);
    }

    [Fact]
    public async Task ApplyAsync_DifferentFingerprint_DiffsAddAndRemove()
    {
        var universe = new ActiveSymbolUniverse();
        var client = new RecordingClient();
        using var coordinator = new BasketSubscriptionCoordinator(
            universe, client, NullLogger<BasketSubscriptionCoordinator>.Instance);

        await coordinator.ApplyAsync(
            BuildSnapshot("fp-1", new[] { "AAPL", "MSFT", "NVDA" }),
            CancellationToken.None);
        await coordinator.ApplyAsync(
            BuildSnapshot("fp-2", new[] { "AAPL", "GOOG" }),
            CancellationToken.None);

        // Two applies → two subscribe calls (first with 3 syms, second with GOOG only)
        Assert.Equal(2, client.Subscribes.Count);
        Assert.Equal(new[] { "GOOG" }, client.Subscribes[1]);

        // Exactly one unsubscribe call for {MSFT, NVDA}.
        Assert.Single(client.Unsubscribes);
        Assert.Equal(new[] { "MSFT", "NVDA" }, client.Unsubscribes[0].OrderBy(s => s).ToArray());

        Assert.Equal("fp-2", coordinator.AppliedFingerprint);
        Assert.Equal(new[] { "AAPL", "GOOG" },
            coordinator.CurrentAppliedSymbols.OrderBy(s => s).ToArray());
    }

    [Fact]
    public void SeedBootstrapSymbols_PopulatesAppliedSetForBoot()
    {
        var universe = new ActiveSymbolUniverse();
        var client = new RecordingClient();
        using var coordinator = new BasketSubscriptionCoordinator(
            universe, client, NullLogger<BasketSubscriptionCoordinator>.Instance);

        coordinator.SeedBootstrapSymbols(new[] { "aapl", "MSFT" });

        Assert.Equal(new[] { "AAPL", "MSFT" },
            coordinator.CurrentAppliedSymbols.OrderBy(s => s).ToArray());
        Assert.Equal("bootstrap:override", coordinator.AppliedFingerprint);
    }

    [Fact]
    public async Task UniverseEvent_TriggersApplyViaHandler()
    {
        var universe = new ActiveSymbolUniverse();
        var client = new RecordingClient();
        using var coordinator = new BasketSubscriptionCoordinator(
            universe, client, NullLogger<BasketSubscriptionCoordinator>.Instance);

        universe.SetFromBasket("HQQQ", "fp-1", new DateOnly(2026, 4, 18),
            new[] { "AAPL" }, "fallback-seed", DateTimeOffset.UtcNow);

        // Handler fires asynchronously; give it a moment.
        for (var i = 0; i < 50 && client.Subscribes.Count == 0; i++)
            await Task.Delay(20);

        Assert.Single(client.Subscribes);
        Assert.Equal("fp-1", coordinator.AppliedFingerprint);
    }

    private static UniverseSnapshot BuildSnapshot(string fingerprint, IEnumerable<string> symbols) =>
        new()
        {
            BasketId = "HQQQ",
            Fingerprint = fingerprint,
            AsOfDate = new DateOnly(2026, 4, 18),
            Symbols = new HashSet<string>(symbols, StringComparer.Ordinal),
            Source = "test",
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

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
