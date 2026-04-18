using Hqqq.Infrastructure.Kafka;
using Hqqq.QuoteEngine.Consumers;
using Hqqq.QuoteEngine.Persistence;
using Hqqq.QuoteEngine.Services;
using Hqqq.QuoteEngine.State;
using Hqqq.QuoteEngine.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hqqq.QuoteEngine.Tests.Persistence;

public class EngineCheckpointRestoreTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 4, 16, 13, 30, 0, TimeSpan.Zero);
    private readonly string _tempDir;

    public EngineCheckpointRestoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hqqq-qe-restore-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    private sealed record Rig(
        Hqqq.QuoteEngine.Services.QuoteEngine Engine,
        BasketStateStore Baskets,
        BasketEventConsumer BasketConsumer,
        FileEngineCheckpointStore Store,
        string CheckpointPath);

    private Rig BuildRig()
    {
        var clock = new FakeSystemClock(T0);
        var options = new QuoteEngineOptions();
        var quotes = new PerSymbolQuoteStore(clock);
        var baskets = new BasketStateStore();
        var runtime = new EngineRuntimeState(options.SeriesCapacity);
        var calculator = new IncrementalNavCalculator(quotes, baskets, runtime, clock, options);
        var snap = new SnapshotMaterializer(quotes, baskets, runtime, clock, options);
        var delta = new QuoteDeltaMaterializer(baskets, runtime, snap, clock);
        var engine = new Hqqq.QuoteEngine.Services.QuoteEngine(
            quotes, baskets, runtime, calculator, snap, delta);

        var sink = new RecordingBasketStateSink();
        var consumer = new BasketEventConsumer(
            Options.Create(new KafkaOptions()),
            options,
            sink,
            NullLogger<BasketEventConsumer>.Instance);

        var path = Path.Combine(_tempDir, "checkpoint.json");
        var store = new FileEngineCheckpointStore(path, NullLogger<FileEngineCheckpointStore>.Instance);

        return new Rig(engine, baskets, consumer, store, path);
    }

    [Fact]
    public async Task Restorer_WithValidCheckpoint_HydratesEngineAndPrimesConsumer()
    {
        var rig = BuildRig();

        var state = new TestActiveBasketStateBuilder()
            .WithBasketId("HQQQ").WithFingerprint("fp-restore").WithScaleFactor(0.001m)
            .AddConstituent("AAPL", "Apple", 1000, 200m, 0.5m)
            .AddConstituent("MSFT", "Microsoft", 500, 400m, 0.5m)
            .Build();

        await rig.Store.SaveAsync(new EngineCheckpoint
        {
            WrittenAtUtc = T0,
            Basket = state,
        }, CancellationToken.None);

        var restorer = new EngineCheckpointRestorer(
            rig.Store, rig.Engine, rig.BasketConsumer,
            NullLogger<EngineCheckpointRestorer>.Instance);

        await restorer.StartAsync(CancellationToken.None);

        Assert.True(rig.Engine.IsInitialized);
        Assert.NotNull(rig.Baskets.Current);
        Assert.Equal("fp-restore", rig.Baskets.Current!.Fingerprint);
        Assert.Equal("fp-restore", rig.BasketConsumer.LastAppliedFingerprint);
    }

    [Fact]
    public async Task Restorer_NoCheckpoint_LeavesEngineUninitialized_AndDoesNotThrow()
    {
        var rig = BuildRig();

        var restorer = new EngineCheckpointRestorer(
            rig.Store, rig.Engine, rig.BasketConsumer,
            NullLogger<EngineCheckpointRestorer>.Instance);

        await restorer.StartAsync(CancellationToken.None);

        Assert.False(rig.Engine.IsInitialized);
        Assert.Null(rig.Baskets.Current);
        Assert.Null(rig.BasketConsumer.LastAppliedFingerprint);
    }

    [Fact]
    public async Task Restorer_CorruptCheckpoint_DoesNotThrow_AndEngineStaysCold()
    {
        var rig = BuildRig();
        await File.WriteAllTextAsync(rig.CheckpointPath, "{ this is not json ");

        var restorer = new EngineCheckpointRestorer(
            rig.Store, rig.Engine, rig.BasketConsumer,
            NullLogger<EngineCheckpointRestorer>.Instance);

        await restorer.StartAsync(CancellationToken.None);

        Assert.False(rig.Engine.IsInitialized);
    }

    [Fact]
    public async Task Restorer_ThenLiveReplayOfSameFingerprint_IsSkipped()
    {
        var rig = BuildRig();

        var state = new TestActiveBasketStateBuilder()
            .WithBasketId("HQQQ").WithFingerprint("fp-once").WithScaleFactor(0.001m)
            .AddConstituent("AAPL", "Apple", 1000, 200m, 1.0m)
            .Build();

        await rig.Store.SaveAsync(new EngineCheckpoint
        {
            WrittenAtUtc = T0,
            Basket = state,
        }, CancellationToken.None);

        var restorer = new EngineCheckpointRestorer(
            rig.Store, rig.Engine, rig.BasketConsumer,
            NullLogger<EngineCheckpointRestorer>.Instance);

        await restorer.StartAsync(CancellationToken.None);
        var appliedOnReplay = await rig.BasketConsumer.HandleAsync(state, CancellationToken.None);

        Assert.False(appliedOnReplay);
    }
}
