using Hqqq.Domain.Services;

namespace Hqqq.QuoteEngine.Tests.Services;

public class FreshnessSummarizerTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 16, 13, 30, 0, TimeSpan.Zero);
    private static readonly TimeSpan Stale = TimeSpan.FromSeconds(30);

    [Fact]
    public void AllFresh_GivesFreshPctOneHundred()
    {
        var tracked = new[] { "AAPL", "MSFT", "NVDA" };
        var obs = new Dictionary<string, DateTimeOffset>
        {
            ["AAPL"] = Now - TimeSpan.FromSeconds(1),
            ["MSFT"] = Now - TimeSpan.FromSeconds(2),
            ["NVDA"] = Now - TimeSpan.FromSeconds(3),
        };

        var summary = FreshnessSummarizer.Summarize(tracked, obs, Now, Stale);

        Assert.Equal(3, summary.SymbolsTotal);
        Assert.Equal(3, summary.SymbolsFresh);
        Assert.Equal(0, summary.SymbolsStale);
        Assert.Equal(100m, summary.FreshPct);
        Assert.Equal(Now - TimeSpan.FromSeconds(1), summary.LastTickUtc);
        Assert.NotNull(summary.AvgTickIntervalMs);
    }

    [Fact]
    public void PartialStale_CountsAndLastTickAreCorrect()
    {
        var tracked = new[] { "AAPL", "MSFT", "NVDA" };
        var obs = new Dictionary<string, DateTimeOffset>
        {
            ["AAPL"] = Now - TimeSpan.FromSeconds(1),
            ["MSFT"] = Now - TimeSpan.FromMinutes(5),
            ["NVDA"] = Now - TimeSpan.FromSeconds(10),
        };

        var summary = FreshnessSummarizer.Summarize(tracked, obs, Now, Stale);

        Assert.Equal(2, summary.SymbolsFresh);
        Assert.Equal(1, summary.SymbolsStale);
        Assert.InRange(summary.FreshPct, 66.6m, 66.7m);
    }

    [Fact]
    public void AllStale_CountsEverythingStale_AndNoDivisionByZero()
    {
        var tracked = new[] { "AAPL", "MSFT" };
        var obs = new Dictionary<string, DateTimeOffset>
        {
            ["AAPL"] = Now - TimeSpan.FromMinutes(2),
            ["MSFT"] = Now - TimeSpan.FromMinutes(3),
        };

        var summary = FreshnessSummarizer.Summarize(tracked, obs, Now, Stale);

        Assert.Equal(0, summary.SymbolsFresh);
        Assert.Equal(2, summary.SymbolsStale);
        Assert.Equal(0m, summary.FreshPct);
    }

    [Fact]
    public void EmptyTrackedList_ReturnsZeros()
    {
        var summary = FreshnessSummarizer.Summarize(
            Array.Empty<string>(),
            new Dictionary<string, DateTimeOffset>(),
            Now,
            Stale);

        Assert.Equal(0, summary.SymbolsTotal);
        Assert.Null(summary.LastTickUtc);
        Assert.Null(summary.AvgTickIntervalMs);
    }

    [Fact]
    public void IsStale_BoundaryIsExclusive()
    {
        var receivedAt = Now - TimeSpan.FromSeconds(30);

        Assert.False(FreshnessSummarizer.IsStale(receivedAt, Now, Stale));
        Assert.True(FreshnessSummarizer.IsStale(
            receivedAt - TimeSpan.FromMilliseconds(1), Now, Stale));
    }
}
