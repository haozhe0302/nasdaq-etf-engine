using Hqqq.Persistence.MarketSession;

namespace Hqqq.Persistence.Tests.MarketSession;

public class RegularSessionClockTests
{
    private static readonly TimeZoneInfo Eastern = RegularSessionClock.ResolveEasternTimeZone();
    private static readonly RegularSessionClock Clock = new(Eastern);

    /// <summary>Builds a UTC instant from an ET wall-clock time on the given date.</summary>
    private static DateTimeOffset Et(int year, int month, int day, int hour, int minute)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        var offset = Eastern.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    [Fact]
    public void BeforeOpen_IsNotRegularSession()
    {
        // 09:29 ET on a Thursday (2026-04-16).
        Assert.False(Clock.IsRegularSessionPoint(Et(2026, 4, 16, 9, 29)));
    }

    [Fact]
    public void AtOpen_IsRegularSession()
    {
        // 09:30 ET inclusive.
        Assert.True(Clock.IsRegularSessionPoint(Et(2026, 4, 16, 9, 30)));
    }

    [Fact]
    public void MidSession_IsRegularSession()
    {
        Assert.True(Clock.IsRegularSessionPoint(Et(2026, 4, 16, 12, 0)));
    }

    [Fact]
    public void AtClose_IsNotRegularSession()
    {
        // 16:00 ET is exclusive — the close itself is out of session.
        Assert.False(Clock.IsRegularSessionPoint(Et(2026, 4, 16, 16, 0)));
    }

    [Fact]
    public void AfterClose_IsNotRegularSession()
    {
        Assert.False(Clock.IsRegularSessionPoint(Et(2026, 4, 16, 16, 1)));
    }

    [Fact]
    public void Saturday_DuringSessionHours_IsNotRegularSession()
    {
        // 2026-04-18 is a Saturday; noon ET must still be out of session.
        Assert.False(Clock.IsRegularSessionPoint(Et(2026, 4, 18, 12, 0)));
    }

    [Fact]
    public void Sunday_DuringSessionHours_IsNotRegularSession()
    {
        // 2026-04-19 is a Sunday.
        Assert.False(Clock.IsRegularSessionPoint(Et(2026, 4, 19, 12, 0)));
    }

    [Fact]
    public void IsRegularSessionOpen_IsAliasForIsRegularSessionPoint()
    {
        var inside = Et(2026, 4, 16, 12, 0);
        var outside = Et(2026, 4, 16, 20, 0);
        Assert.Equal(Clock.IsRegularSessionPoint(inside), Clock.IsRegularSessionOpen(inside));
        Assert.Equal(Clock.IsRegularSessionPoint(outside), Clock.IsRegularSessionOpen(outside));
    }
}
