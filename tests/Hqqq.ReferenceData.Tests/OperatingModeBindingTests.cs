using Hqqq.Infrastructure.Hosting;
using Microsoft.Extensions.Configuration;

namespace Hqqq.ReferenceData.Tests;

/// <summary>
/// Mirrors the ingress mode-binding contract for reference-data: the
/// service must respect <c>HQQQ_OPERATING_MODE=standalone</c> the same
/// way ingress does so the two services swap between hybrid and
/// standalone in lockstep.
/// </summary>
public class OperatingModeBindingTests
{
    [Fact]
    public void DefaultsToHybrid()
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
