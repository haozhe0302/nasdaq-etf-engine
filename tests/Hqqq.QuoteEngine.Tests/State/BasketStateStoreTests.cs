using Hqqq.QuoteEngine.State;
using Hqqq.QuoteEngine.Tests.Fakes;

namespace Hqqq.QuoteEngine.Tests.State;

public class BasketStateStoreTests
{
    [Fact]
    public void Current_IsNullBeforeFirstReplace()
    {
        var store = new BasketStateStore();
        Assert.Null(store.Current);
    }

    [Fact]
    public void Replace_InstallsBasket_AndIsReadableBack()
    {
        var store = new BasketStateStore();
        var basket = new TestBasketBuilder()
            .AddConstituent("AAPL", "Apple", shares: 1000, referencePrice: 170m, weight: 0.6m)
            .AddConstituent("MSFT", "Microsoft", shares: 500, referencePrice: 400m, weight: 0.4m)
            .Build();

        store.Replace(basket);

        Assert.NotNull(store.Current);
        Assert.Equal(basket.Fingerprint, store.Current!.Fingerprint);
        Assert.Equal(2, store.Current.Constituents.Count);
    }

    [Fact]
    public void Replace_ReplacesBasketEntirely()
    {
        var store = new BasketStateStore();
        var first = new TestBasketBuilder()
            .WithFingerprint("fp-1")
            .AddConstituent("AAPL", "Apple", 1000, 170m, 1.0m)
            .Build();
        var second = new TestBasketBuilder()
            .WithFingerprint("fp-2")
            .AddConstituent("MSFT", "Microsoft", 500, 400m, 1.0m)
            .Build();

        store.Replace(first);
        store.Replace(second);

        Assert.Equal("fp-2", store.Current!.Fingerprint);
        Assert.Single(store.Current.Constituents);
        Assert.Equal("MSFT", store.Current.Constituents[0].Symbol);
    }
}
