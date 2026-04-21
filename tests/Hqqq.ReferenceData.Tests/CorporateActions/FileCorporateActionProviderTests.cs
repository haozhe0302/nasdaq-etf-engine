using Hqqq.ReferenceData.Configuration;
using Hqqq.ReferenceData.CorporateActions.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Tests.CorporateActions;

public class FileCorporateActionProviderTests
{
    [Fact]
    public async Task FetchAsync_DefaultsToEmbeddedSeed_ReturnsEmptyFeed()
    {
        // The committed embedded seed is an empty object — a successful
        // parse + empty feed is the correct wire up test here.
        var provider = BuildProvider(path: null);

        var feed = await provider.FetchAsync(
            symbols: Array.Empty<string>(),
            from: new DateOnly(2026, 1, 1),
            to: new DateOnly(2026, 12, 31),
            ct: CancellationToken.None);

        Assert.Equal("file", feed.Source);
        Assert.Empty(feed.Splits);
        Assert.Empty(feed.Renames);
        Assert.Null(feed.Error);
    }

    [Fact]
    public async Task FetchAsync_FilePath_ParsesSplitsAndRenamesInWindow()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """
                {
                  "splits": [
                    { "symbol": "aapl", "effectiveDate": "2026-04-17", "factor": 4.0, "description": "4:1" },
                    { "symbol": "MSFT", "effectiveDate": "2025-01-01", "factor": 2.0 }
                  ],
                  "renames": [
                    { "oldSymbol": "FB", "newSymbol": "META", "effectiveDate": "2026-04-18" }
                  ]
                }
                """);

            var provider = BuildProvider(path: path);
            var feed = await provider.FetchAsync(
                symbols: new[] { "AAPL", "META", "FB" },
                from: new DateOnly(2026, 4, 1),
                to: new DateOnly(2026, 4, 30),
                ct: CancellationToken.None);

            Assert.Single(feed.Splits); // MSFT split is out of window; AAPL in-window.
            Assert.Equal("AAPL", feed.Splits[0].Symbol);
            Assert.Equal(4m, feed.Splits[0].Factor);

            Assert.Single(feed.Renames);
            Assert.Equal("FB", feed.Renames[0].OldSymbol);
            Assert.Equal("META", feed.Renames[0].NewSymbol);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task FetchAsync_MissingFile_ReturnsErrorFeed()
    {
        var provider = BuildProvider(path: "Z:/definitely-not-there/corp.json");

        var feed = await provider.FetchAsync(
            symbols: Array.Empty<string>(),
            from: new DateOnly(2026, 1, 1),
            to: new DateOnly(2026, 12, 31),
            ct: CancellationToken.None);

        Assert.Empty(feed.Splits);
        Assert.Empty(feed.Renames);
        Assert.NotNull(feed.Error);
    }

    [Fact]
    public async Task FetchAsync_IgnoresFactorOfOneAndNonPositive()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, """
                {
                  "splits": [
                    { "symbol": "NOOP", "effectiveDate": "2026-04-17", "factor": 1.0 },
                    { "symbol": "BAD", "effectiveDate": "2026-04-17", "factor": -2.0 }
                  ],
                  "renames": []
                }
                """);

            var provider = BuildProvider(path: path);
            var feed = await provider.FetchAsync(
                symbols: Array.Empty<string>(),
                from: new DateOnly(2026, 4, 1),
                to: new DateOnly(2026, 4, 30),
                ct: CancellationToken.None);

            Assert.Empty(feed.Splits);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static FileCorporateActionProvider BuildProvider(string? path)
    {
        var options = Options.Create(new ReferenceDataOptions
        {
            CorporateActions = new CorporateActionOptions
            {
                File = new FileCorporateActionOptions { Path = path },
            },
        });
        return new FileCorporateActionProvider(
            options, NullLogger<FileCorporateActionProvider>.Instance);
    }
}
