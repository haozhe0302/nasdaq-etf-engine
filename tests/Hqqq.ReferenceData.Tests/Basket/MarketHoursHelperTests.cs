using Hqqq.ReferenceData.Basket;
using Microsoft.Extensions.Time.Testing;

namespace Hqqq.ReferenceData.Tests.Basket;

/// <summary>
/// Covers the Phase-2-native <see cref="MarketHoursHelper"/> port.
/// Zone resolution tolerates both IANA and Windows ids; the market
/// open/close window maps correctly to 09:30-16:00 local and weekends
/// read as closed.
/// </summary>
public class MarketHoursHelperTests
{
    private static readonly TimeZoneInfo Eastern =
        MarketHoursHelper.ResolveTimeZone("America/New_York");

    [Fact]
    public void IsMarketOpen_Midweek_InsideSession_ReturnsTrue()
    {
        // Wednesday 2026-04-15 10:00 ET
        var openInUtc = new DateTimeOffset(2026, 4, 15, 14, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(openInUtc);
        var helper = new MarketHoursHelper(Eastern, clock);

        Assert.True(helper.IsMarketOpen());
    }

    [Fact]
    public void IsMarketOpen_Saturday_ReturnsFalse()
    {
        // Saturday 2026-04-18 10:00 ET
        var saturday = new DateTimeOffset(2026, 4, 18, 14, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(saturday);
        var helper = new MarketHoursHelper(Eastern, clock);

        Assert.False(helper.IsMarketOpen());
    }

    [Fact]
    public void IsMarketOpen_PreOpen_ReturnsFalse()
    {
        // Wednesday 2026-04-15 09:00 ET
        var preOpen = new DateTimeOffset(2026, 4, 15, 13, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(preOpen);
        var helper = new MarketHoursHelper(Eastern, clock);

        Assert.False(helper.IsMarketOpen());
    }

    [Fact]
    public void ResolveTimeZone_InvalidInput_FallsBackSafely()
    {
        // Should never throw — falls back through the candidate list
        // to UTC as the last resort.
        var tz = MarketHoursHelper.ResolveTimeZone("<bogus>");
        Assert.NotNull(tz);
    }
}
