using Hqqq.QuoteEngine.State;
using Hqqq.QuoteEngine.Tests.Fakes;

namespace Hqqq.QuoteEngine.Tests.State;

public class PerSymbolQuoteStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 4, 16, 13, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Update_OverwritesPreviousPrice_ButKeepsOptionalFields()
    {
        var clock = new FakeSystemClock(T0);
        var store = new PerSymbolQuoteStore(clock);

        store.Update(TestBasketBuilder.Tick("AAPL", 170m, T0, previousClose: 168m, sequence: 1));

        clock.Advance(TimeSpan.FromSeconds(1));
        store.Update(TestBasketBuilder.Tick("AAPL", 171m, clock.UtcNow, previousClose: null, sequence: 2));

        var state = store.Get("AAPL");
        Assert.NotNull(state);
        Assert.Equal(171m, state!.Price);
        Assert.Equal(2L, state.Sequence);
        Assert.Equal(168m, state.PreviousClose);
        Assert.Equal(clock.UtcNow, state.ReceivedAtUtc);
    }

    [Fact]
    public void Get_IsCaseInsensitive()
    {
        var store = new PerSymbolQuoteStore(new FakeSystemClock(T0));
        store.Update(TestBasketBuilder.Tick("msft", 400m, T0));

        Assert.NotNull(store.Get("MSFT"));
        Assert.NotNull(store.Get("Msft"));
    }

    [Fact]
    public void BuildFreshnessSummary_TreatsMissingSymbolsAsStale()
    {
        var clock = new FakeSystemClock(T0);
        var store = new PerSymbolQuoteStore(clock);

        store.Update(TestBasketBuilder.Tick("AAPL", 170m, T0));
        store.Update(TestBasketBuilder.Tick("MSFT", 400m, T0));

        clock.Advance(TimeSpan.FromSeconds(5));

        var summary = store.BuildFreshnessSummary(
            new[] { "AAPL", "MSFT", "NVDA" },
            TimeSpan.FromSeconds(30));

        Assert.Equal(3, summary.SymbolsTotal);
        Assert.Equal(2, summary.SymbolsFresh);
        Assert.Equal(1, summary.SymbolsStale);
        Assert.Equal(T0, summary.LastTickUtc);
    }

    [Fact]
    public void BuildFreshnessSummary_RespectsStaleThreshold()
    {
        var clock = new FakeSystemClock(T0);
        var store = new PerSymbolQuoteStore(clock);

        store.Update(TestBasketBuilder.Tick("AAPL", 170m, T0));
        clock.Advance(TimeSpan.FromSeconds(1));
        store.Update(TestBasketBuilder.Tick("MSFT", 400m, clock.UtcNow));

        // Jump 31s past T0 so AAPL is stale but MSFT is still within 30s window.
        clock.SetTo(T0 + TimeSpan.FromSeconds(31));

        var summary = store.BuildFreshnessSummary(
            new[] { "AAPL", "MSFT" }, TimeSpan.FromSeconds(30));

        Assert.Equal(1, summary.SymbolsFresh);
        Assert.Equal(1, summary.SymbolsStale);
    }

    [Fact]
    public void BuildFreshnessSummary_EmptyBasket_ReturnsZeros()
    {
        var store = new PerSymbolQuoteStore(new FakeSystemClock(T0));

        var summary = store.BuildFreshnessSummary(
            Array.Empty<string>(), TimeSpan.FromSeconds(30));

        Assert.Equal(0, summary.SymbolsTotal);
        Assert.Equal(0, summary.SymbolsFresh);
        Assert.Equal(0, summary.SymbolsStale);
        Assert.Null(summary.LastTickUtc);
    }

    [Fact]
    public void GetRollingAvgTickIntervalMs_ReturnsNullBelowTwoSamples()
    {
        var clock = new FakeSystemClock(T0);
        var store = new PerSymbolQuoteStore(clock);

        Assert.Null(store.GetRollingAvgTickIntervalMs());

        store.Update(TestBasketBuilder.Tick("AAPL", 170m, clock.UtcNow));
        Assert.Null(store.GetRollingAvgTickIntervalMs());
    }

    [Fact]
    public void GetRollingAvgTickIntervalMs_AveragesConsecutiveArrivalGaps()
    {
        var clock = new FakeSystemClock(T0);
        var store = new PerSymbolQuoteStore(clock);

        // Ticks every 500ms across symbols → avg gap 500ms.
        for (var i = 0; i < 5; i++)
        {
            store.Update(TestBasketBuilder.Tick($"S{i}", 100m + i, clock.UtcNow));
            clock.Advance(TimeSpan.FromMilliseconds(500));
        }

        Assert.Equal(500d, store.GetRollingAvgTickIntervalMs());
    }

    [Fact]
    public void GetRollingAvgTickIntervalMs_StaysStable_WhenSomeSymbolsGoStale()
    {
        // Regression: the old per-symbol last-seen spacing metric drifted
        // upward forever as symbols stopped ticking. The rolling arrival
        // window must reflect only recent throughput.
        var clock = new FakeSystemClock(T0);
        var store = new PerSymbolQuoteStore(clock);

        // A burst from a symbol that then goes permanently stale.
        store.Update(TestBasketBuilder.Tick("STALE", 10m, clock.UtcNow));
        clock.Advance(TimeSpan.FromMilliseconds(100));
        store.Update(TestBasketBuilder.Tick("STALE", 11m, clock.UtcNow));

        // Long quiet gap, then a steady 200ms-cadence stream from another
        // symbol — enough samples to flush the early burst from the window.
        clock.Advance(TimeSpan.FromMinutes(30));
        for (var i = 0; i < 600; i++)
        {
            store.Update(TestBasketBuilder.Tick("LIVE", 50m + i, clock.UtcNow));
            clock.Advance(TimeSpan.FromMilliseconds(200));
        }

        var avg = store.GetRollingAvgTickIntervalMs();
        Assert.NotNull(avg);
        // Should reflect the recent 200ms cadence, not the 30-minute gap.
        Assert.InRange(avg!.Value, 199d, 201d);
    }
}
