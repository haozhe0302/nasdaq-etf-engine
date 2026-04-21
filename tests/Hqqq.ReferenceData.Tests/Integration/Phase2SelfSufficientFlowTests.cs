extern alias IngressService;

using Hqqq.Contracts.Events;
using Hqqq.Infrastructure.Kafka;
using IngressService::Hqqq.Ingress.Clients;
using IngressService::Hqqq.Ingress.Configuration;
using IngressService::Hqqq.Ingress.Consumers;
using IngressService::Hqqq.Ingress.Publishing;
using IngressService::Hqqq.Ingress.State;
using IngressService::Hqqq.Ingress.Workers;
using Hqqq.ReferenceData.Publishing;
using Hqqq.ReferenceData.Sources;
using Hqqq.ReferenceData.Tests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Tests.Integration;

/// <summary>
/// Phase 2 self-sufficient end-to-end test, executed entirely in-process
/// with no broker and no <c>hqqq-api</c> participation.
///
/// Exercises the full hot-path wiring:
/// <list type="number">
///   <item>real <see cref="Hqqq.ReferenceData.Services.BasketRefreshPipeline"/>
///         activates a basket from a deterministic snapshot;</item>
///   <item>a bridge <see cref="IBasketPublisher"/> hands the resulting
///         <see cref="BasketActiveStateV1"/> directly to
///         <see cref="BasketActiveConsumer.Apply"/> (the same projection
///         the Kafka consumer uses);</item>
///   <item>the basket lands in <c>ActiveSymbolUniverse</c> and
///         <c>BasketSubscriptionCoordinator</c> diffs the subscription
///         set against the shared Tiingo stream client double;</item>
///   <item><see cref="TiingoIngressWorker"/> resolves the universe,
///         opens the (fake) websocket loop, and the publish-and-record
///         callback advances <see cref="IngestionState.PublishedTickCount"/>.</item>
/// </list>
///
/// The asserts pin the contract that the audit cared about: ingress
/// owns ticks, ingress consumes Phase-2-published baskets, and there is
/// no monolith in the loop.
/// </summary>
public class Phase2SelfSufficientFlowTests
{
    [Fact(Timeout = 15_000)]
    public async Task RefDataActivatesBasket_IngressSubscribesAndPublishesTicks()
    {
        // ── ingress side: real universe + coordinator + state ───────────────
        var universe = new IngressService::Hqqq.Ingress.State.ActiveSymbolUniverse();
        var state = new IngestionState();
        var streamClient = new EmittingStreamClient(emitOnConnect: 3);
        var coordinator = new BasketSubscriptionCoordinator(
            universe, streamClient, NullLogger<BasketSubscriptionCoordinator>.Instance);

        var basketConsumer = new BasketActiveConsumer(
            universe,
            Options.Create(new KafkaOptions()),
            Options.Create(new IngressBasketOptions()),
            NullLogger<BasketActiveConsumer>.Instance);

        var tickPublisher = new RecordingTickPublisher();

        var worker = new TiingoIngressWorker(
            streamClient: streamClient,
            snapshotClient: new NoOpSnapshotClient(),
            publisher: tickPublisher,
            state: state,
            universe: universe,
            coordinator: coordinator,
            tiingoOptions: Options.Create(new TiingoOptions
            {
                ApiKey = "real-looking-key",
                SnapshotOnStartup = false,
            }),
            basketOptions: Options.Create(new IngressBasketOptions
            {
                StartupWaitSeconds = 5,
            }),
            logger: NullLogger<TiingoIngressWorker>.Instance);

        // ── refdata side: real pipeline + bridge publisher ──────────────────
        var bridge = new ConsumerBridgePublisher(basketConsumer);
        var bench = PipelineBuilder.BuildWithPublisher(bridge);
        bench.Source.Enqueue(HoldingsFetchResult.Ok(SnapshotBuilder.Build(count: 60)));

        // Start the worker first so it is parked in ResolveInitialSymbolsAsync
        // when the basket arrives. StartAsync only validates preconditions;
        // ExecuteAsync runs on the host scheduler when we let the loop drive.
        using var cts = new CancellationTokenSource();
        await worker.StartAsync(cts.Token);
        var execTask = Task.Run(() => InvokeExecuteAsync(worker, cts.Token));

        // Trigger the real refdata refresh — the bridge publisher synchronously
        // pushes the wire event into the ingress consumer.
        var refresh = await bench.Pipeline.RefreshAsync(CancellationToken.None);
        Assert.True(refresh.Success);
        Assert.True(refresh.Changed);
        Assert.Single(bridge.Published);
        Assert.NotNull(universe.Current);
        Assert.Equal(60, universe.Current!.Symbols.Count);

        // Wait for the worker → coordinator → fake-stream-client subscribe
        // to complete and for the synthetic ticks to flow through the
        // publish-and-record callback.
        await SpinUntilAsync(() => state.PublishedTickCount >= 3);

        Assert.Equal(60, coordinator.CurrentAppliedSymbols.Count);
        Assert.Equal(refresh.Fingerprint, coordinator.AppliedFingerprint);
        Assert.True(streamClient.SubscribesReceived.Count >= 1,
            "ingress did not call ITiingoStreamClient.ConnectAndStreamAsync");
        Assert.True(state.PublishedTickCount >= 3,
            $"ingress did not publish at least 3 ticks; PublishedTickCount={state.PublishedTickCount}");
        Assert.NotNull(state.LastPublishedTickUtc);
        Assert.Equal(state.PublishedTickCount, tickPublisher.PublishedTicks.Count);

        await cts.CancelAsync();
        try { await execTask; } catch (OperationCanceledException) { }
        await worker.StopAsync(CancellationToken.None);
    }

    private static Task InvokeExecuteAsync(TiingoIngressWorker worker, CancellationToken ct)
    {
        // ExecuteAsync is protected; the BackgroundService base would run it
        // when the host starts. For a unit-style integration test we drive
        // it via reflection so we don't need a full IHost.
        var method = typeof(Microsoft.Extensions.Hosting.BackgroundService)
            .GetMethod(
                "ExecuteAsync",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        return (Task)method.Invoke(worker, new object[] { ct })!;
    }

    private static async Task SpinUntilAsync(Func<bool> predicate, int maxMs = 5_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(maxMs);
        while (DateTime.UtcNow < deadline)
        {
            if (predicate()) return;
            await Task.Delay(20);
        }
    }

    /// <summary>
    /// Bridges the real refdata <see cref="IBasketPublisher"/> contract to
    /// the ingress <see cref="BasketActiveConsumer.Apply"/> projection so
    /// the integration test can prove the wire-format hand-off without a
    /// Kafka broker. Mirrors what the real consumer does on every poll.
    /// </summary>
    private sealed class ConsumerBridgePublisher : IBasketPublisher
    {
        private readonly BasketActiveConsumer _consumer;
        private readonly List<BasketActiveStateV1> _published = new();

        public ConsumerBridgePublisher(BasketActiveConsumer consumer) { _consumer = consumer; }

        public IReadOnlyList<BasketActiveStateV1> Published => _published;

        public Task PublishAsync(BasketActiveStateV1 state, CancellationToken ct)
        {
            _published.Add(state);
            _consumer.Apply(state);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Fake Tiingo stream client that emits a fixed number of synthetic
    /// ticks per connect call so the integration test can observe the
    /// ingress publish-and-record path advancing
    /// <see cref="IngestionState.PublishedTickCount"/>.
    /// </summary>
    private sealed class EmittingStreamClient : ITiingoStreamClient
    {
        private readonly int _emitOnConnect;

        public EmittingStreamClient(int emitOnConnect) { _emitOnConnect = emitOnConnect; }

        public bool IsConnected { get; private set; }
        public DateTimeOffset? LastDataUtc => null;

        public List<string[]> SubscribesReceived { get; } = new();
        public List<string[]> Unsubscribes { get; } = new();

        public async Task ConnectAndStreamAsync(
            IEnumerable<string> symbols,
            Func<RawTickV1, CancellationToken, Task> onTick,
            CancellationToken ct)
        {
            var symArr = symbols.ToArray();
            SubscribesReceived.Add(symArr);
            IsConnected = true;
            try
            {
                var emit = Math.Min(_emitOnConnect, Math.Max(1, symArr.Length));
                for (var i = 0; i < emit; i++)
                {
                    var sym = symArr[i % symArr.Length];
                    var now = DateTimeOffset.UtcNow;
                    await onTick(new RawTickV1
                    {
                        Symbol = sym,
                        Last = 100m + i,
                        Currency = "USD",
                        Provider = "tiingo",
                        ProviderTimestamp = now,
                        IngressTimestamp = now,
                        Sequence = i + 1,
                    }, ct).ConfigureAwait(false);
                }

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
            SubscribesReceived.Add(symbols.ToArray());
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

    private sealed class RecordingTickPublisher : ITickPublisher
    {
        private readonly List<RawTickV1> _published = new();
        public IReadOnlyList<RawTickV1> PublishedTicks
        {
            get { lock (_published) return _published.ToArray(); }
        }

        public Task PublishAsync(RawTickV1 tick, CancellationToken ct)
        {
            lock (_published) _published.Add(tick);
            return Task.CompletedTask;
        }

        public Task PublishBatchAsync(IEnumerable<RawTickV1> ticks, CancellationToken ct)
        {
            lock (_published) _published.AddRange(ticks);
            return Task.CompletedTask;
        }
    }
}
