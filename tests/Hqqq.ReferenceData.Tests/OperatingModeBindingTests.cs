using Hqqq.Infrastructure.Hosting;
using Microsoft.Extensions.Configuration;

namespace Hqqq.ReferenceData.Tests;

/// <summary>
/// <c>OperatingMode</c> is now a logging-posture tag (runtime behaviour no
/// longer branches on it across Phase 2 services). This test simply pins
/// the value-binding contract so a regression that breaks the
/// <c>OperatingMode</c> env / config resolution is caught early.
/// </summary>
public class OperatingModeBindingTests
{
    [Fact]
    public void DefaultsToHybridForBackwardCompatibility()
    {
        var config = new ConfigurationBuilder().Build();
        Assert.Equal(OperatingMode.Hybrid, OperatingModeRegistration.ResolveMode(config));
    }

    [Fact]
    public void ResolvesStandaloneFromScalarKey()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OperatingMode"] = "standalone" })
            .Build();
        Assert.Equal(OperatingMode.Standalone, OperatingModeRegistration.ResolveMode(config));
    }
}
