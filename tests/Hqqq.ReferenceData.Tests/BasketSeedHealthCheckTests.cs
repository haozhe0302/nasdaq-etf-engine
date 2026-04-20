using Hqqq.ReferenceData.Health;
using Hqqq.ReferenceData.Repositories;
using Hqqq.ReferenceData.Standalone;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Hqqq.ReferenceData.Tests;

/// <summary>
/// Verifies the <c>/healthz/ready</c> payload exposed by the standalone
/// reference-data health check carries the as-of metadata operators
/// rely on (basketId, version, asOfDate, fingerprint, count, source).
/// </summary>
public class BasketSeedHealthCheckTests
{
    [Fact]
    public async Task CheckHealthAsync_ReportsHealthyAndExposesSeedMetadata()
    {
        var seed = new BasketSeed
        {
            BasketId = "HQQQ",
            Version = "v-test",
            AsOfDate = new DateOnly(2026, 4, 15),
            ScaleFactor = 1.0m,
            Fingerprint = "abc123" + new string('0', 58),
            Source = "test://memory",
            Constituents = new List<BasketSeedConstituent>
            {
                new() { Symbol = "AAPL", Name = "Apple", Sector = "Technology", SharesHeld = 1, ReferencePrice = 100m },
                new() { Symbol = "MSFT", Name = "Microsoft", Sector = "Technology", SharesHeld = 1, ReferencePrice = 200m },
            },
        };
        var repo = new SeedFileBasketRepository(seed);
        var check = new BasketSeedHealthCheck(repo);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("HQQQ", result.Data["basketId"]);
        Assert.Equal("v-test", result.Data["version"]);
        Assert.Equal("2026-04-15", result.Data["asOfDate"]);
        Assert.Equal(seed.Fingerprint, result.Data["fingerprint"]);
        Assert.Equal(2, result.Data["constituentCount"]);
        Assert.Equal(1.0m, result.Data["scaleFactor"]);
        Assert.Equal("test://memory", result.Data["source"]);
        Assert.True(result.Data.ContainsKey("activatedAtUtc"));
        Assert.Contains("HQQQ", result.Description);
    }
}
