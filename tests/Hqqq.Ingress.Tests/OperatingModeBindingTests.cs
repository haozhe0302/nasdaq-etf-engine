using Hqqq.Infrastructure.Hosting;
using Microsoft.Extensions.Configuration;

namespace Hqqq.Ingress.Tests;

/// <summary>
/// Verifies the <c>HQQQ_OPERATING_MODE</c> environment variable / config
/// surface that gates ingress between hybrid (stub) and standalone
/// (real Tiingo) behaviour.
/// </summary>
public class OperatingModeBindingTests
{
    [Fact]
    public void ResolveMode_DefaultsToHybrid_WhenUnset()
    {
        var config = new ConfigurationBuilder().Build();
        Assert.Equal(OperatingMode.Hybrid, OperatingModeRegistration.ResolveMode(config));
    }

    [Theory]
    [InlineData("standalone")]
    [InlineData("Standalone")]
    [InlineData("STANDALONE")]
    public void ResolveMode_ParsesStandaloneCaseInsensitive(string raw)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OperatingMode"] = raw })
            .Build();
        Assert.Equal(OperatingMode.Standalone, OperatingModeRegistration.ResolveMode(config));
    }

    [Fact]
    public void ResolveMode_AcceptsNestedKey()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OperatingMode:Mode"] = "Standalone",
            })
            .Build();
        Assert.Equal(OperatingMode.Standalone, OperatingModeRegistration.ResolveMode(config));
    }

    [Fact]
    public void LegacyFlatKey_MapsHqqqOperatingModeIntoConfig()
    {
        // The shim only fills hierarchical keys when they're missing, so
        // we set the flat key in process env and let the shim re-publish
        // it under the canonical name.
        Environment.SetEnvironmentVariable("HQQQ_OPERATING_MODE", "standalone");
        try
        {
            var builder = new ConfigurationBuilder();
            builder.AddLegacyFlatKeyFallback();
            builder.AddEnvironmentVariables();
            var config = builder.Build();

            Assert.Equal(OperatingMode.Standalone, OperatingModeRegistration.ResolveMode(config));
        }
        finally
        {
            Environment.SetEnvironmentVariable("HQQQ_OPERATING_MODE", null);
        }
    }
}
