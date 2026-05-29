using Hqqq.Gateway.Services.Timescale;

namespace Hqqq.Gateway.Tests.Timescale;

public class HistoryRangeMapTests
{
    private static readonly DateTimeOffset Today =
        new(2026, 4, 17, 14, 30, 0, TimeSpan.Zero); // a Friday

    [Fact]
    public void Resolve_1D_AnchorsBothBoundsOnToday()
    {
        Assert.True(HistoryRangeMap.TryResolve("1D", Today, out var norm, out var from, out var to));

        Assert.Equal("1D", norm);
        Assert.Equal(new DateTimeOffset(2026, 4, 17, 0, 0, 0, TimeSpan.Zero), from);
        // toUtc is the end-of-day bounding value; the SQL applies `ts < @to_utc`.
        Assert.Equal(2026, to.Year);
        Assert.Equal(4, to.Month);
        Assert.Equal(17, to.Day);
        Assert.Equal(23, to.Hour);
        Assert.Equal(59, to.Minute);
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        Assert.True(HistoryRangeMap.TryResolve("1d", Today, out var norm, out _, out _));
        Assert.Equal("1D", norm);
    }

    [Theory]
    [InlineData("5D", 4)]
    [InlineData("1Y", 365)]
    public void Resolve_FromIsBeforeTo(string range, int minDaysSpan)
    {
        Assert.True(HistoryRangeMap.TryResolve(range, Today, out _, out var from, out var to));
        Assert.True(from < to);
        Assert.True((to.Date - from.Date).Days >= minDaysSpan - 1);
    }

    [Theory]
    [InlineData("2Y")]
    [InlineData("")]
    [InlineData(null)]
    public void Resolve_UnsupportedRange_ReturnsFalse(string? range)
    {
        Assert.False(HistoryRangeMap.TryResolve(range, Today, out _, out _, out _));
    }
}
