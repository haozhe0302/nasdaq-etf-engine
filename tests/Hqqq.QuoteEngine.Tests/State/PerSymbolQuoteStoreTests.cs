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
}
