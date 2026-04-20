using Hqqq.ReferenceData.Standalone;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Tests;

/// <summary>
/// Validates the standalone basket seed loader: embedded fallback works,
/// fingerprint is deterministic across processes, and validation errors
/// surface as startup-friendly <see cref="InvalidOperationException"/>s.
/// </summary>
public class BasketSeedLoaderTests
{
    [Fact]
    public void Load_FromEmbeddedResource_ReturnsValidatedSeed()
    {
        var loader = BuildLoader(seedPath: null);
        var seed = loader.Load();

        Assert.Equal("HQQQ", seed.BasketId);
        Assert.False(string.IsNullOrWhiteSpace(seed.Version));
        Assert.True(seed.Constituents.Count >= 25,
            $"expected at least 25 constituents, got {seed.Constituents.Count}");
        Assert.True(seed.ScaleFactor > 0);
        Assert.False(string.IsNullOrWhiteSpace(seed.Fingerprint));
        Assert.Equal(64, seed.Fingerprint.Length); // SHA-256 hex
        Assert.StartsWith("resource://", seed.Source);
        Assert.Contains(seed.Constituents, c => c.Symbol == "AAPL");
    }

    [Fact]
    public void Load_FromOverridePath_ReadsThatFile()
    {
        var json = SampleSeedJson("OVR", "v-override");
        var path = WriteTempSeed(json);
        try
        {
            var loader = BuildLoader(seedPath: path);
            var seed = loader.Load();

            Assert.Equal("OVR", seed.BasketId);
            Assert.Equal("v-override", seed.Version);
            Assert.Equal(path, seed.Source);
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

    [Fact]
    public void ComputeFingerprint_IsStableAcrossInvocations()
    {
        var file = LoadSampleFile();
        var first = BasketSeedLoader.ComputeFingerprint(file);
        var second = BasketSeedLoader.ComputeFingerprint(file);
        Assert.Equal(first, second);
    }

    [Fact]
    public void ComputeFingerprint_IsStableAcrossConstituentOrder()
    {
        var file = LoadSampleFile();
        var reordered = new BasketSeedFile
        {
            BasketId = file.BasketId,
            Version = file.Version,
            AsOfDate = file.AsOfDate,
            ScaleFactor = file.ScaleFactor,
            NavPreviousClose = file.NavPreviousClose,
            QqqPreviousClose = file.QqqPreviousClose,
            Constituents = file.Constituents
                .OrderByDescending(c => c.Symbol, StringComparer.Ordinal)
                .ToList(),
        };

        Assert.Equal(
            BasketSeedLoader.ComputeFingerprint(file),
            BasketSeedLoader.ComputeFingerprint(reordered));
    }

    [Fact]
    public void ComputeFingerprint_ChangesWhenAReferencePriceChanges()
    {
        var a = LoadSampleFile();
        var b = LoadSampleFile();
        b.Constituents[0].ReferencePrice = a.Constituents[0].ReferencePrice + 1m;

        Assert.NotEqual(
            BasketSeedLoader.ComputeFingerprint(a),
            BasketSeedLoader.ComputeFingerprint(b));
    }

    [Theory]
    [InlineData("basketId is required", "basketId")]
    [InlineData("version is required", "version")]
    [InlineData("scaleFactor must be > 0", "scaleFactor")]
    [InlineData("constituents must be non-empty", "constituents")]
    public void Load_FailsValidation_WhenRequiredFieldsMissing(string expectedFragment, string fieldToBlank)
    {
        // Sanity-check the error surfaces operator-friendly hints rather
        // than a JSON exception.
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

    private static BasketSeedLoader BuildLoader(string? seedPath)
        => new(
            Options.Create(new BasketSeedOptions { SeedPath = seedPath }),
            NullLogger<BasketSeedLoader>.Instance);

    private static BasketSeedFile LoadSampleFile()
    {
        // Use the embedded seed as a stable fixture so the test data
        // tracks the production seed shape automatically.
        var json = BasketSeedLoader.LoadEmbedded(typeof(BasketSeedLoader).Assembly);
        return System.Text.Json.JsonSerializer.Deserialize<BasketSeedFile>(json)!;
    }

    private static string WriteTempSeed(string json)
    {
        var path = Path.Combine(Path.GetTempPath(),
            $"hqqq-seed-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    private static string SampleSeedJson(
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
