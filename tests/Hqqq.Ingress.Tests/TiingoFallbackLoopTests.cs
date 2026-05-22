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
/// Locks down the production-grade Tiingo REST fallback. Each test drives a
/// single <see cref="TiingoFallbackLoop.PollOnceAsync"/> tick against scripted
/// clients so the decision boundaries are deterministic without spinning up
/// timers or real sockets.
/// </summary>
public class TiingoFallbackLoopTests
{
    [Fact]
    public async Task WhenWebSocketConnectedAndFresh_PollIsStandby_AndFallbackIsInactive()
    {
        var state = new IngestionState();
        state.SetWebSocketConnected(true);
        state.RecordTick(); // fresh activity
        state.SetFallbackActive(true); // simulate prior fallback that should now stand down

        var (loop, fakes) = BuildLoop(state, symbols: new[] { "AAPL", "MSFT" });

        var outcome = await loop.PollOnceAsync(CancellationToken.None);

        Assert.Equal(FallbackTickOutcome.Standby, outcome);
        Assert.False(state.IsFallbackActive);
        Assert.Empty(fakes.Snapshot.Calls);
        Assert.Empty(fakes.Publisher.Batches);
    }

    [Fact]
    public async Task WhenWebSocketDisconnected_PollFetchesAndPublishes_AndFallbackBecomesActive()
    {
        var state = new IngestionState();
        state.SetWebSocketConnected(false);

        var (loop, fakes) = BuildLoop(state, symbols: new[] { "AAPL", "MSFT" });
        fakes.Snapshot.QueueResponse(new[]
        {
            BuildTick("AAPL", 200m),
            BuildTick("MSFT", 400m),
        });

        var outcome = await loop.PollOnceAsync(CancellationToken.None);

        Assert.Equal(FallbackTickOutcome.Published, outcome);
        Assert.True(state.IsFallbackActive);
        Assert.Single(fakes.Snapshot.Calls);
        Assert.Equal(new[] { "AAPL", "MSFT" }, fakes.Snapshot.Calls[0].OrderBy(s => s).ToArray());
        Assert.Single(fakes.Publisher.Batches);
        Assert.Equal(2, fakes.Publisher.Batches[0].Length);
        Assert.Equal(2, state.PublishedTickCount);
        Assert.Equal(1, state.FallbackPollSuccessCount);
        Assert.NotNull(state.LastFallbackPollUtc);
    }

    [Fact]
    public async Task WhenWebSocketConnectedButStale_PollFetchesAndPublishes()
    {
        // A connected but quiet socket must still trigger the fallback so the
        // pipeline stays alive when Tiingo silently stops delivering frames.
        var state = new IngestionState();
        state.SetWebSocketConnected(true);
        // Don't record any tick; LastActivityUtc stays null which the loop
        // treats as "connected but never delivered" → fallback armed.

        var options = new TiingoOptions
        {
            FallbackActivationStalenessSeconds = 5,
            StaleAfterSeconds = 60,
            RestPollingIntervalSeconds = 15,
        };
        var (loop, fakes) = BuildLoop(state, symbols: new[] { "AAPL" }, options: options);
        fakes.Snapshot.QueueResponse(new[] { BuildTick("AAPL", 200m) });

        var outcome = await loop.PollOnceAsync(CancellationToken.None);

        Assert.Equal(FallbackTickOutcome.Published, outcome);
        Assert.True(state.IsFallbackActive);
    }

    [Fact]
    public async Task WhenNoSymbolsAvailable_PollSkipsAndMarksFallbackActive()
    {
        var state = new IngestionState();
        state.SetWebSocketConnected(false);

        var (loop, fakes) = BuildLoop(state, symbols: Array.Empty<string>());

        var outcome = await loop.PollOnceAsync(CancellationToken.None);

        Assert.Equal(FallbackTickOutcome.SkippedNoSymbols, outcome);
        Assert.True(state.IsFallbackActive);
        Assert.Empty(fakes.Snapshot.Calls);
        Assert.Empty(fakes.Publisher.Batches);
    }

    [Fact]
    public async Task WhenSnapshotFetchThrows_PollFailsAndRecordsError()
    {
        var state = new IngestionState();
        state.SetWebSocketConnected(false);

        var (loop, fakes) = BuildLoop(state, symbols: new[] { "AAPL" });
        fakes.Snapshot.ThrowOnNextCall(new InvalidOperationException("REST 503"));

        var outcome = await loop.PollOnceAsync(CancellationToken.None);

        Assert.Equal(FallbackTickOutcome.Failed, outcome);
        Assert.True(state.IsFallbackActive);
        Assert.Equal(0, state.PublishedTickCount);
        Assert.Equal(1, state.FallbackPollFailureCount);
        Assert.NotNull(state.LastError);
        Assert.Contains("REST 503", state.LastError);
    }

    [Fact]
    public async Task WhenPublishThrows_PollFailsAndRecordsError()
    {
        var state = new IngestionState();
        state.SetWebSocketConnected(false);

        var (loop, fakes) = BuildLoop(state, symbols: new[] { "AAPL" });
        fakes.Snapshot.QueueResponse(new[] { BuildTick("AAPL", 200m) });
        fakes.Publisher.ThrowOnNextBatch(new InvalidOperationException("kafka down"));

        var outcome = await loop.PollOnceAsync(CancellationToken.None);

        Assert.Equal(FallbackTickOutcome.Failed, outcome);
        Assert.True(state.IsFallbackActive);
        Assert.Equal(0, state.PublishedTickCount);
        Assert.Equal(1, state.FallbackPollFailureCount);
        Assert.Contains("kafka down", state.LastError);
    }

    [Fact]
    public async Task WhenSnapshotReturnsEmpty_PollIsFailedAndDoesNotAdvancePublishCount()
    {
        var state = new IngestionState();
        state.SetWebSocketConnected(false);

        var (loop, fakes) = BuildLoop(state, symbols: new[] { "AAPL" });
        fakes.Snapshot.QueueResponse(Array.Empty<RawTickV1>());

        var outcome = await loop.PollOnceAsync(CancellationToken.None);

        Assert.Equal(FallbackTickOutcome.Failed, outcome);
        Assert.True(state.IsFallbackActive);
        Assert.Equal(0, state.PublishedTickCount);
        Assert.Equal(1, state.FallbackPollFailureCount);
        Assert.Empty(fakes.Publisher.Batches);
    }

    [Fact]
    public async Task BasketAddedBeforePoll_PollUsesUpdatedSymbols()
    {
        var state = new IngestionState();
        state.SetWebSocketConnected(false);
        var (loop, fakes) = BuildLoop(state, symbols: new[] { "AAPL" });

        // First poll covers AAPL only.
        fakes.Snapshot.QueueResponse(new[] { BuildTick("AAPL", 200m) });
        await loop.PollOnceAsync(CancellationToken.None);
        Assert.Equal(new[] { "AAPL" }, fakes.Snapshot.Calls[0]);

        // Basket adds MSFT mid-session — coordinator should reflect it on
        // the next poll without any restart.
        await fakes.Universe.UpdateBasketAsync(fakes.Coordinator, new[] { "AAPL", "MSFT" });
        fakes.Snapshot.QueueResponse(new[]
        {
            BuildTick("AAPL", 201m),
            BuildTick("MSFT", 400m),
        });

        await loop.PollOnceAsync(CancellationToken.None);

        Assert.Equal(new[] { "AAPL", "MSFT" }, fakes.Snapshot.Calls[1].OrderBy(s => s).ToArray());
    }

    [Fact]
    public async Task WhenWebSocketReconnects_NextPollDeactivatesFallback()
    {
        var state = new IngestionState();
        state.SetWebSocketConnected(false);
        var (loop, fakes) = BuildLoop(state, symbols: new[] { "AAPL" });

        // First tick activates fallback.
        fakes.Snapshot.QueueResponse(new[] { BuildTick("AAPL", 200m) });
        await loop.PollOnceAsync(CancellationToken.None);
        Assert.True(state.IsFallbackActive);

        // WS reconnects and starts delivering ticks.
        state.SetWebSocketConnected(true);
        state.RecordTick();

        var outcome = await loop.PollOnceAsync(CancellationToken.None);

        Assert.Equal(FallbackTickOutcome.Standby, outcome);
        Assert.False(state.IsFallbackActive);
        // No additional REST call issued.
        Assert.Single(fakes.Snapshot.Calls);
    }

    private static RawTickV1 BuildTick(string symbol, decimal price)
        => new()
        {
            Symbol = symbol,
            Last = price,
            Bid = null,
            Ask = null,
            Currency = "USD",
            Provider = "tiingo-iex",
            ProviderTimestamp = DateTimeOffset.UtcNow,
            IngressTimestamp = DateTimeOffset.UtcNow,
            Sequence = 1,
        };

    private static (TiingoFallbackLoop loop, FakeBundle fakes) BuildLoop(
        IngestionState state,
        IReadOnlyCollection<string> symbols,
        TiingoOptions? options = null)
    {
        var snapshot = new FakeSnapshotClient();
        var publisher = new RecordingPublisher();
        var streamClient = new TiingoIngressWorkerStartupTests.FakeTiingoStreamClient();
        var universe = new ActiveSymbolUniverse();
        var coordinator = new BasketSubscriptionCoordinator(
            universe, streamClient, NullLogger<BasketSubscriptionCoordinator>.Instance);
        if (symbols.Count > 0)
            coordinator.SeedBootstrapSymbols(symbols);

        options ??= new TiingoOptions
        {
            ApiKey = "test-key",
            RestPollingIntervalSeconds = 15,
            FallbackActivationStalenessSeconds = 30,
            StaleAfterSeconds = 60,
        };

        var loop = new TiingoFallbackLoop(
            snapshot, publisher, state, coordinator,
            Options.Create(options),
            NullLogger<TiingoFallbackLoop>.Instance);

        return (loop, new FakeBundle(snapshot, publisher, universe, coordinator));
    }

    private sealed record FakeBundle(
        FakeSnapshotClient Snapshot,
        RecordingPublisher Publisher,
        ActiveSymbolUniverse Universe,
        BasketSubscriptionCoordinator Coordinator);

    private sealed class FakeSnapshotClient : ITiingoSnapshotClient
    {
        private readonly Queue<IReadOnlyList<RawTickV1>> _responses = new();
        private Exception? _nextException;

        public List<string[]> Calls { get; } = new();

        public void QueueResponse(IReadOnlyList<RawTickV1> response) => _responses.Enqueue(response);
        public void ThrowOnNextCall(Exception ex) => _nextException = ex;

        public Task<IReadOnlyList<RawTickV1>> FetchSnapshotsAsync(IEnumerable<string> symbols, CancellationToken ct)
        {
            Calls.Add(symbols.ToArray());
            if (_nextException is { } ex)
            {
                _nextException = null;
                throw ex;
            }
            if (_responses.Count == 0) return Task.FromResult<IReadOnlyList<RawTickV1>>(Array.Empty<RawTickV1>());
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class RecordingPublisher : ITickPublisher
    {
        public List<RawTickV1[]> Batches { get; } = new();
        private Exception? _nextException;

        public void ThrowOnNextBatch(Exception ex) => _nextException = ex;

        public Task PublishAsync(RawTickV1 tick, CancellationToken ct)
        {
            Batches.Add(new[] { tick });
            return Task.CompletedTask;
        }

        public Task PublishBatchAsync(IEnumerable<RawTickV1> ticks, CancellationToken ct)
        {
            if (_nextException is { } ex)
            {
                _nextException = null;
                throw ex;
            }
            Batches.Add(ticks.ToArray());
            return Task.CompletedTask;
        }
    }
}

file static class ActiveSymbolUniverseTestExtensions
{
    /// <summary>
    /// Convenience helper used by the fallback tests to push a new basket
    /// through the universe and synchronously apply it to the coordinator —
    /// matches what the real <c>BasketActiveConsumer</c> does at runtime
    /// without spinning up Kafka.
    /// </summary>
    public static async Task UpdateBasketAsync(
        this ActiveSymbolUniverse universe,
        BasketSubscriptionCoordinator coordinator,
        IEnumerable<string> symbols)
    {
        var symbolList = symbols.ToArray();
        universe.SetFromBasket(
            basketId: "HQQQ",
            fingerprint: "fp-" + Guid.NewGuid().ToString("N"),
            asOfDate: DateOnly.FromDateTime(DateTime.UtcNow),
            symbols: symbolList,
            source: "test",
            updatedAtUtc: DateTimeOffset.UtcNow);
        await coordinator.ApplyAsync(universe.Current!, CancellationToken.None);
    }
}
