using Hqqq.Ingress.State;

namespace Hqqq.Ingress.Tests;

public class IngestionStateTests
{
    [Fact]
    public void RecordError_PopulatesLastErrorAndTimestamp()
    {
        var state = new IngestionState();
        Assert.Null(state.LastError);
        Assert.Null(state.LastErrorAtUtc);

        state.RecordError("boom");

        Assert.Equal("boom", state.LastError);
        Assert.NotNull(state.LastErrorAtUtc);
        Assert.True(state.LastErrorAtUtc <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public void IsUpstreamConnected_TracksWebSocketFlag()
    {
        var state = new IngestionState();
        Assert.False(state.IsUpstreamConnected);

        state.SetWebSocketConnected(true);
        Assert.True(state.IsUpstreamConnected);

        state.SetWebSocketConnected(false);
        Assert.False(state.IsUpstreamConnected);
    }

    [Fact]
    public void RecordTick_UpdatesCounterAndActivityTimestamp()
    {
        var state = new IngestionState();
        Assert.Equal(0, state.TicksIngested);

        state.RecordTick();
        state.RecordTick();

        Assert.Equal(2, state.TicksIngested);
        Assert.NotNull(state.LastActivityUtc);
    }
}
