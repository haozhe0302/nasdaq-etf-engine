using System.Text.Json;
using Hqqq.Contracts.Events;

namespace Hqqq.Contracts.Tests;

public class SmokeTests
{
    [Fact]
    public void ProjectCompiles_ReturnsTrue()
    {
        Assert.True(true);
    }

    [Fact]
    public void RawTickV1_CanSerializeAndDeserialize()
    {
        var tick = new RawTickV1
        {
            Symbol = "AAPL",
            Last = 150.25m,
            Currency = "USD",
            Provider = "tiingo",
            ProviderTimestamp = DateTimeOffset.UtcNow,
            IngressTimestamp = DateTimeOffset.UtcNow,
            Sequence = 1,
        };

        var json = JsonSerializer.Serialize(tick);
        var restored = JsonSerializer.Deserialize<RawTickV1>(json);

        Assert.NotNull(restored);
        Assert.Equal(tick.Symbol, restored.Symbol);
        Assert.Equal(tick.Last, restored.Last);
    }
}
