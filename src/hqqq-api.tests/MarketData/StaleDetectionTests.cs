using Hqqq.Api.Configuration;
using Hqqq.Api.Modules.MarketData.Contracts;
using Hqqq.Api.Modules.MarketData.Services;
using Hqqq.Api.Modules.System.Services;
using Microsoft.Extensions.Options;

namespace Hqqq.Api.Tests.MarketData;

public class StaleDetectionTests
{
    private InMemoryLatestPriceStore CreateStore(int staleAfterSeconds = 5)
    {
        var options = Options.Create(new TiingoOptions
        {
            StaleAfterSeconds = staleAfterSeconds,
        });
        return new InMemoryLatestPriceStore(options, new MetricsService());
    }

    private static PriceTick MakeTick(string symbol, decimal price) =>
        new()
        {
            Symbol = symbol,
            Price = price,
            Currency = "USD",
            Source = "ws",
            EventTimeUtc = DateTimeOffset.UtcNow,
        };

    [Fact]
    public void FreshPrice_IsNotStale()
    {
        var store = CreateStore(staleAfterSeconds: 5);
        store.Update(MakeTick("AAPL", 200m));

        var state = store.Get("AAPL");

        Assert.NotNull(state);
        Assert.False(state.IsStale);
    }

    [Fact]
    public void Get_ReturnsNull_ForUnknownSymbol()
    {
        var store = CreateStore();

        var state = store.Get("UNKNOWN");
        Assert.Null(state);
    }

    [Fact]
    public void HealthSnapshot_CountsStaleSymbols()
    {
        var store = CreateStore(staleAfterSeconds: 5);

        var roles = new Dictionary<string, SymbolRole>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAPL"] = SymbolRole.Active,
            ["MSFT"] = SymbolRole.Active,
        };
        store.SetTrackedSymbols(roles);

        store.Update(MakeTick("AAPL", 200m));

        var health = store.GetHealthSnapshot();

        Assert.Equal(2, health.SymbolsTracked);
        Assert.Equal(1, health.SymbolsWithPrice);
        Assert.True(health.StaleSymbolCount >= 1, "MSFT has no price, should be stale");
    }

    [Fact]
    public void Update_OverwritesPreviousPrice()
    {
        var store = CreateStore();
        store.Update(MakeTick("AAPL", 200m));
        store.Update(MakeTick("AAPL", 210m));

        var state = store.Get("AAPL");
        Assert.Equal(210m, state!.Price);
    }

    [Fact]
    public void SetTrackedSymbols_RemovesUntrackedPrices()
    {
        var store = CreateStore();
        store.Update(MakeTick("AAPL", 200m));
        store.Update(MakeTick("MSFT", 400m));

        var roles = new Dictionary<string, SymbolRole>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAPL"] = SymbolRole.Active,
        };
        store.SetTrackedSymbols(roles);

        Assert.NotNull(store.Get("AAPL"));
        Assert.Null(store.Get("MSFT"));
    }

    [Fact]
    public void HealthSnapshot_PendingReadiness_RequiresHighCoverage()
    {
        var store = CreateStore(staleAfterSeconds: 5);

        var roles = new Dictionary<string, SymbolRole>(StringComparer.OrdinalIgnoreCase)
        {
            ["A1"] = SymbolRole.Pending,
            ["A2"] = SymbolRole.Pending,
            ["A3"] = SymbolRole.Pending,
            ["A4"] = SymbolRole.Pending,
        };
        store.SetTrackedSymbols(roles);

        store.Update(MakeTick("A1", 10m));
        store.Update(MakeTick("A2", 20m));
        store.Update(MakeTick("A3", 30m));

        var health = store.GetHealthSnapshot();

        Assert.False(health.IsPendingBasketReady,
            "75% coverage should not satisfy the 95% threshold");
    }

    [Fact]
    public void HealthSnapshot_PendingReady_WhenAllPriced()
    {
        var store = CreateStore(staleAfterSeconds: 5);

        var roles = new Dictionary<string, SymbolRole>(StringComparer.OrdinalIgnoreCase)
        {
            ["A1"] = SymbolRole.Pending,
            ["A2"] = SymbolRole.Pending,
        };
        store.SetTrackedSymbols(roles);

        store.Update(MakeTick("A1", 10m));
        store.Update(MakeTick("A2", 20m));

        var health = store.GetHealthSnapshot();

        Assert.True(health.IsPendingBasketReady);
    }

    [Fact]
    public void GetAll_ReturnsAllTrackedPrices()
    {
        var store = CreateStore();
        store.Update(MakeTick("AAPL", 200m));
        store.Update(MakeTick("MSFT", 400m));

        var all = store.GetAll();

        Assert.Equal(2, all.Count);
        Assert.True(all.ContainsKey("AAPL"));
        Assert.True(all.ContainsKey("MSFT"));
    }

    [Fact]
    public void PreviousClose_IsPreservedAcrossUpdates()
    {
        var store = CreateStore();

        store.Update(new PriceTick
        {
            Symbol = "AAPL",
            Price = 200m,
            Currency = "USD",
            Source = "rest",
            EventTimeUtc = DateTimeOffset.UtcNow,
            PreviousClose = 195m,
        });

        store.Update(new PriceTick
        {
            Symbol = "AAPL",
            Price = 205m,
            Currency = "USD",
            Source = "ws",
            EventTimeUtc = DateTimeOffset.UtcNow,
            PreviousClose = null,
        });

        var state = store.Get("AAPL");
        Assert.Equal(205m, state!.Price);
        Assert.Equal(195m, state.PreviousClose);
    }
}
