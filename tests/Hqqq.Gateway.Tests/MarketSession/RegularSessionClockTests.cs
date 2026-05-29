using Hqqq.Gateway.Services.MarketSession;

namespace Hqqq.Gateway.Tests.MarketSession;

public class RegularSessionClockTests
{
    private static readonly TimeZoneInfo Eastern = RegularSessionClock.ResolveEasternTimeZone();
    private static readonly RegularSessionClock Clock = new(Eastern);

    private static DateTimeOffset Et(int year, int month, int day, int hour, int minute)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        var offset = Eastern.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    [Fact]
    public void BeforeOpen_IsNotRegularSession()
    {
        Assert.False(Clock.IsRegularSessionPoint(Et(2026, 4, 16, 9, 29)));
    }

    [Fact]
    public void AtOpen_IsRegularSession()
    {
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
        Assert.False(Clock.IsRegularSessionPoint(Et(2026, 4, 16, 16, 0)));
    }

    [Fact]
    public void AfterClose_IsNotRegularSession()
    {
        Assert.False(Clock.IsRegularSessionPoint(Et(2026, 4, 16, 16, 30)));
    }

    [Fact]
    public void Saturday_IsNotRegularSession()
    {
        Assert.False(Clock.IsRegularSessionPoint(Et(2026, 4, 18, 12, 0)));
    }

    [Fact]
    public void Sunday_IsNotRegularSession()
    {
        Assert.False(Clock.IsRegularSessionPoint(Et(2026, 4, 19, 12, 0)));
    }

    [Theory]
    [InlineData(9, 24, false)]
    [InlineData(9, 25, true)]
    [InlineData(9, 29, true)]
    [InlineData(9, 30, false)] // open belongs to the session, not the reset window
    [InlineData(12, 0, false)]
    public void PreOpenResetWindow_BoundsAreCorrect(int hour, int minute, bool expected)
    {
        Assert.Equal(expected, Clock.IsPreOpenResetWindow(Et(2026, 4, 16, hour, minute)));
    }

    [Fact]
    public void PreOpenResetWindow_OnWeekend_IsFalse()
    {
        Assert.False(Clock.IsPreOpenResetWindow(Et(2026, 4, 18, 9, 27)));
    }

    [Fact]
    public void GetRegularSessionWindowForEtDate_ReturnsOpenAndClose()
    {
        var (openUtc, closeUtc) = Clock.GetRegularSessionWindowForEtDate(new DateOnly(2026, 4, 16));

        Assert.Equal(Et(2026, 4, 16, 9, 30), openUtc);
        Assert.Equal(Et(2026, 4, 16, 16, 0), closeUtc);
    }

    [Fact]
    public void MostRecentCompletedSession_AfterCloseToday_IsToday()
    {
        // Thursday 17:00 ET — today's session has completed.
        var (openUtc, closeUtc) = Clock.GetMostRecentCompletedRegularSessionWindow(Et(2026, 4, 16, 17, 0));

        Assert.Equal(Et(2026, 4, 16, 9, 30), openUtc);
        Assert.Equal(Et(2026, 4, 16, 16, 0), closeUtc);
    }

    [Fact]
    public void MostRecentCompletedSession_DuringSession_IsPriorWeekday()
    {
        // Thursday 12:00 ET — today's session has not completed; expect Wednesday.
        var (openUtc, closeUtc) = Clock.GetMostRecentCompletedRegularSessionWindow(Et(2026, 4, 16, 12, 0));

        Assert.Equal(Et(2026, 4, 15, 9, 30), openUtc);
        Assert.Equal(Et(2026, 4, 15, 16, 0), closeUtc);
    }

    [Fact]
    public void MostRecentCompletedSession_OnSunday_IsFriday()
    {
        // Sunday 2026-04-19 → most recent completed session is Friday 2026-04-17.
        var (openUtc, closeUtc) = Clock.GetMostRecentCompletedRegularSessionWindow(Et(2026, 4, 19, 10, 0));

        Assert.Equal(Et(2026, 4, 17, 9, 30), openUtc);
        Assert.Equal(Et(2026, 4, 17, 16, 0), closeUtc);
    }

    [Fact]
    public void MostRecentCompletedSession_EarlyMonday_IsFriday()
    {
        // Monday 2026-04-20 08:00 ET (before open) → Friday 2026-04-17.
        var (openUtc, closeUtc) = Clock.GetMostRecentCompletedRegularSessionWindow(Et(2026, 4, 20, 8, 0));

        Assert.Equal(Et(2026, 4, 17, 9, 30), openUtc);
        Assert.Equal(Et(2026, 4, 17, 16, 0), closeUtc);
    }

    [Fact]
    public void DstTransitionDay_MidSession_IsRegularSession()
    {
        // 2026-03-08 is the US spring-forward Sunday; Monday 2026-03-09 is the
        // first EDT weekday. Noon ET is well clear of the 02:00→03:00 gap and
        // must register as in-session under the shifted offset.
        Assert.True(Clock.IsRegularSessionPoint(Et(2026, 3, 9, 12, 0)));
    }
}
