using System.Text.Json;
using Hqqq.Api.Configuration;
using Hqqq.Api.Modules.Pricing.Contracts;
using Hqqq.Api.Modules.Pricing.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hqqq.Api.Tests.Pricing;

public class ScaleStatePersistenceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;
    private readonly JsonScaleStateStore _store;

    public ScaleStatePersistenceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"hqqq-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "scale-state.json");

        var options = Options.Create(new PricingOptions
        {
            ScaleStateFilePath = _filePath,
        });
        _store = new JsonScaleStateStore(options, NullLogger<JsonScaleStateStore>.Instance);
    }

    [Fact]
    public async Task Load_ReturnsUninitialized_WhenFileDoesNotExist()
    {
        var state = await _store.LoadAsync(CancellationToken.None);

        Assert.False(state.IsInitialized);
        Assert.Equal(0m, state.ScaleFactor);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrips()
    {
        var original = new ScaleState
        {
            ScaleFactor = 0.000042m,
            BasketFingerprint = "ABC123",
            PricingBasisFingerprint = "DEF456",
            ActivatedAtUtc = new DateTimeOffset(2026, 3, 27, 14, 0, 0, TimeSpan.Zero),
            ComputedAtUtc = new DateTimeOffset(2026, 3, 27, 14, 0, 0, TimeSpan.Zero),
            InferredTotalNotional = 100_000_000_000m,
            BasisEntries =
            [
                new PricingBasisEntry
                {
                    Symbol = "AAPL",
                    Shares = 45000,
                    ReferencePrice = 200m,
                    SharesOrigin = "official",
                    TargetWeight = 0.30m,
                },
            ],
        };

        await _store.SaveAsync(original, CancellationToken.None);
        var loaded = await _store.LoadAsync(CancellationToken.None);

        Assert.True(loaded.IsInitialized);
        Assert.Equal(original.ScaleFactor, loaded.ScaleFactor);
        Assert.Equal(original.BasketFingerprint, loaded.BasketFingerprint);
        Assert.Equal(original.PricingBasisFingerprint, loaded.PricingBasisFingerprint);
        Assert.Single(loaded.BasisEntries);
        Assert.Equal("AAPL", loaded.BasisEntries[0].Symbol);
        Assert.Equal(45000, loaded.BasisEntries[0].Shares);
    }

    [Fact]
    public async Task Load_ReturnsUninitialized_WhenFileIsCorrupted()
    {
        await File.WriteAllTextAsync(_filePath, "NOT VALID JSON {{{");

        var state = await _store.LoadAsync(CancellationToken.None);

        Assert.False(state.IsInitialized);
    }

    [Fact]
    public async Task Save_CreatesDirectory_WhenMissing()
    {
        var deepPath = Path.Combine(_tempDir, "sub", "dir", "state.json");
        var options = Options.Create(new PricingOptions
        {
            ScaleStateFilePath = deepPath,
        });
        var store = new JsonScaleStateStore(options, NullLogger<JsonScaleStateStore>.Instance);

        var state = new ScaleState
        {
            ScaleFactor = 1m,
            BasketFingerprint = "test",
            PricingBasisFingerprint = "test",
            ActivatedAtUtc = DateTimeOffset.UtcNow,
            ComputedAtUtc = DateTimeOffset.UtcNow,
            InferredTotalNotional = 1m,
            BasisEntries = [],
        };

        await store.SaveAsync(state, CancellationToken.None);

        Assert.True(File.Exists(deepPath));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort cleanup */ }
    }
}
