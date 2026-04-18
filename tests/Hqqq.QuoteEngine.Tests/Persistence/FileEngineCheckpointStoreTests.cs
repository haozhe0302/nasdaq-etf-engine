using Hqqq.QuoteEngine.Persistence;
using Hqqq.QuoteEngine.State;
using Hqqq.QuoteEngine.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hqqq.QuoteEngine.Tests.Persistence;

public class FileEngineCheckpointStoreTests : IDisposable
{
    private readonly string _tempDir;

    public FileEngineCheckpointStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hqqq-qe-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    private FileEngineCheckpointStore NewStore(string? subPath = null)
    {
        var path = Path.Combine(_tempDir, subPath ?? "checkpoint.json");
        return new FileEngineCheckpointStore(path, NullLogger<FileEngineCheckpointStore>.Instance);
    }

    private static EngineCheckpoint SampleCheckpoint()
    {
        var state = new TestActiveBasketStateBuilder()
            .WithBasketId("HQQQ").WithFingerprint("fp-xyz").WithScaleFactor(0.001m)
            .AddConstituent("AAPL", "Apple", 1000, 200m, 0.5m)
            .AddConstituent("MSFT", "Microsoft", 500, 400m, 0.5m)
            .Build();

        return new EngineCheckpoint
        {
            WrittenAtUtc = new DateTimeOffset(2026, 4, 16, 14, 0, 0, TimeSpan.Zero),
            Basket = state,
            LastSnapshot = new SnapshotDigest
            {
                Nav = 600m,
                Qqq = 500m,
                PremiumDiscountPct = -0.25m,
                ComputedAtUtc = new DateTimeOffset(2026, 4, 16, 13, 59, 55, TimeSpan.Zero),
            },
        };
    }

    [Fact]
    public async Task SaveThenLoad_RoundTrips()
    {
        var store = NewStore();
        var checkpoint = SampleCheckpoint();

        await store.SaveAsync(checkpoint, CancellationToken.None);
        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal("HQQQ", loaded!.Basket.BasketId);
        Assert.Equal("fp-xyz", loaded.Basket.Fingerprint);
        Assert.Equal(0.001m, loaded.Basket.ScaleFactor);
        Assert.Equal(2, loaded.Basket.Constituents.Count);
        Assert.Equal(2, loaded.Basket.PricingBasis.Entries.Count);
        Assert.NotNull(loaded.LastSnapshot);
        Assert.Equal(600m, loaded.LastSnapshot!.Nav);
        Assert.Equal(checkpoint.WrittenAtUtc, loaded.WrittenAtUtc);
    }

    [Fact]
    public async Task Load_MissingFile_ReturnsNull()
    {
        var store = NewStore("does-not-exist.json");

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task Load_CorruptJson_ReturnsNullAndDoesNotThrow()
    {
        var path = Path.Combine(_tempDir, "corrupt.json");
        await File.WriteAllTextAsync(path, "{ not-json ");
        var store = new FileEngineCheckpointStore(path, NullLogger<FileEngineCheckpointStore>.Instance);

        var loaded = await store.LoadAsync(CancellationToken.None);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task Save_IsAtomic_DoesNotLeavePartialFileVisible()
    {
        var store = NewStore("atomic.json");
        await store.SaveAsync(SampleCheckpoint(), CancellationToken.None);

        var finalPath = Path.Combine(_tempDir, "atomic.json");
        var tempPath = finalPath + ".tmp";

        Assert.True(File.Exists(finalPath));
        Assert.False(File.Exists(tempPath));
    }

    [Fact]
    public async Task Save_CreatesMissingDirectory()
    {
        var nested = Path.Combine(_tempDir, "nested", "dir", "cp.json");
        var store = new FileEngineCheckpointStore(nested, NullLogger<FileEngineCheckpointStore>.Instance);

        await store.SaveAsync(SampleCheckpoint(), CancellationToken.None);

        Assert.True(File.Exists(nested));
    }
}
