using Hqqq.ReferenceData.Configuration;
using Hqqq.ReferenceData.Sources;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Tests.Sources;

/// <summary>
/// Validates the committed basket-seed loader: embedded fallback works,
/// the seed is a realistic ~100-name basket, and validation errors
/// surface as startup-friendly <see cref="InvalidOperationException"/>s.
/// </summary>
public class BasketSeedLoaderTests
{
    [Fact]
    public void Load_FromEmbeddedResource_ReturnsValidatedSeed()
    {
        var loader = BuildLoader(seedPath: null);
        var snapshot = loader.Load();

        Assert.Equal("HQQQ", snapshot.BasketId);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Version));
        // Soft lower bound: the committed seed ships with ~100 names and
        // the service tolerates drift around that number. We assert >= 90
        // rather than == 100 so the test is not brittle to index-event
        // adjustments.
        Assert.True(snapshot.Constituents.Count >= 90,
            $"expected at least 90 constituents, got {snapshot.Constituents.Count}");
        Assert.True(snapshot.ScaleFactor > 0);
        Assert.Equal(BasketSeedLoader.SourceTag, snapshot.Source);
        Assert.Contains(snapshot.Constituents, c => c.Symbol == "AAPL");
        Assert.Contains(snapshot.Constituents, c => c.Symbol == "NVDA");
        Assert.All(snapshot.Constituents, c => Assert.True(c.SharesHeld > 0));
        Assert.All(snapshot.Constituents, c => Assert.True(c.ReferencePrice > 0));
    }

    [Fact]
    public void Load_FromOverridePath_ReadsThatFile()
    {
        var json = SampleSeedJson("OVR", "v-override");
        var path = WriteTempSeed(json);
        try
        {
            var loader = BuildLoader(seedPath: path);
            var snapshot = loader.Load();

            Assert.Equal("OVR", snapshot.BasketId);
            Assert.Equal("v-override", snapshot.Version);
            Assert.Equal(BasketSeedLoader.SourceTag, snapshot.Source);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_WhenOverridePathMissing_ThrowsInvalidOperation()
    {
        var loader = BuildLoader(seedPath: Path.Combine(Path.GetTempPath(), "definitely-not-here.json"));
        var ex = Assert.Throws<InvalidOperationException>(() => loader.Load());
        Assert.Contains("does not exist", ex.Message);
    }

    [Theory]
    [InlineData("basketId is required", "basketId")]
    [InlineData("version is required", "version")]
    [InlineData("scaleFactor must be > 0", "scaleFactor")]
    [InlineData("constituents must be non-empty", "constituents")]
    public void Load_FailsValidation_WhenRequiredFieldsMissing(string expectedFragment, string fieldToBlank)
    {
        var json = BuildBrokenSeedJson(fieldToBlank);
        var path = WriteTempSeed(json);
        try
        {
            var loader = BuildLoader(seedPath: path);
            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load());
            Assert.Contains(expectedFragment, ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_FailsValidation_WhenAsOfDateMalformed()
    {
        var json = SampleSeedJson("HQQQ", "v1", asOfDate: "not-a-date");
        var path = WriteTempSeed(json);
        try
        {
            var loader = BuildLoader(seedPath: path);
            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load());
            Assert.Contains("asOfDate", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_FailsValidation_WhenDuplicateSymbol()
    {
        var json = """
        {
          "basketId": "HQQQ",
          "version": "v1",
          "asOfDate": "2026-04-15",
          "scaleFactor": 1.0,
          "constituents": [
            { "symbol": "AAPL", "name": "Apple", "sector": "Technology", "sharesHeld": 1, "referencePrice": 100 },
            { "symbol": "AAPL", "name": "Apple Dup", "sector": "Technology", "sharesHeld": 2, "referencePrice": 200 }
          ]
        }
        """;
        var path = WriteTempSeed(json);
        try
        {
            var loader = BuildLoader(seedPath: path);
            var ex = Assert.Throws<InvalidOperationException>(() => loader.Load());
            Assert.Contains("duplicate symbol", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    internal static BasketSeedLoader BuildLoader(string? seedPath)
        => new(
            Options.Create(new ReferenceDataOptions { SeedPath = seedPath }),
            NullLogger<BasketSeedLoader>.Instance);

    internal static string WriteTempSeed(string json)
    {
        var path = Path.Combine(Path.GetTempPath(),
            $"hqqq-seed-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    internal static string SampleSeedJson(
        string basketId, string version, string asOfDate = "2026-04-15")
    {
        return $$"""
        {
          "basketId": "{{basketId}}",
          "version": "{{version}}",
          "asOfDate": "{{asOfDate}}",
          "scaleFactor": 1.0,
          "constituents": [
            { "symbol": "AAPL", "name": "Apple Inc.", "sector": "Technology", "sharesHeld": 100, "referencePrice": 215.30, "targetWeight": 0.5 },
            { "symbol": "MSFT", "name": "Microsoft Corp.", "sector": "Technology", "sharesHeld": 100, "referencePrice": 432.10, "targetWeight": 0.5 }
          ]
        }
        """;
    }

    private static string BuildBrokenSeedJson(string fieldToBlank)
    {
        var basketId = fieldToBlank == "basketId" ? "" : "HQQQ";
        var version = fieldToBlank == "version" ? "" : "v1";
        var scale = fieldToBlank == "scaleFactor" ? "0" : "1.0";
        var constituents = fieldToBlank == "constituents"
            ? "[]"
            : """[ { "symbol": "AAPL", "name": "Apple", "sector": "Technology", "sharesHeld": 1, "referencePrice": 100 } ]""";

        return $$"""
        {
          "basketId": "{{basketId}}",
          "version": "{{version}}",
          "asOfDate": "2026-04-15",
          "scaleFactor": {{scale}},
          "constituents": {{constituents}}
        }
        """;
    }
}
