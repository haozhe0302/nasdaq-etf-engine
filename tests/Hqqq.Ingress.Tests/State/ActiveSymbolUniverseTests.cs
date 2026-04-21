using Hqqq.Ingress.State;

namespace Hqqq.Ingress.Tests.State;

public class ActiveSymbolUniverseTests
{
    [Fact]
    public void SetFromBasket_PublishesEvent_OnFingerprintChange()
    {
        var universe = new ActiveSymbolUniverse();
        UniverseSnapshot? received = null;
        universe.BasketUpdated += s => received = s;

        universe.SetFromBasket(
            basketId: "HQQQ",
            fingerprint: "fp-1",
            asOfDate: new DateOnly(2026, 4, 18),
            symbols: new[] { "aapl", "MSFT", "aapl" },
            source: "fallback-seed",
            updatedAtUtc: DateTimeOffset.UtcNow);

        Assert.NotNull(universe.Current);
        Assert.Equal("HQQQ", universe.Current!.BasketId);
        Assert.Equal("fp-1", universe.Current.Fingerprint);
        Assert.Equal(new[] { "AAPL", "MSFT" }, universe.Current.Symbols.OrderBy(s => s).ToArray());
        Assert.NotNull(received);
        Assert.Same(received, universe.Current);
    }

    [Fact]
    public void SetFromBasket_SameFingerprint_DoesNotRepublishEvent()
    {
        var universe = new ActiveSymbolUniverse();
        var count = 0;
        universe.BasketUpdated += _ => count++;

        var when = DateTimeOffset.UtcNow;

        universe.SetFromBasket("HQQQ", "fp-1", new DateOnly(2026, 4, 18),
            new[] { "AAPL" }, "fallback-seed", when);
        universe.SetFromBasket("HQQQ", "fp-1", new DateOnly(2026, 4, 18),
            new[] { "AAPL" }, "fallback-seed", when.AddSeconds(1));

        Assert.Equal(1, count);
        Assert.Equal(when.AddSeconds(1), universe.Current!.UpdatedAtUtc);
    }

    [Fact]
    public void SetFromBasket_NewFingerprint_Republishes()
    {
        var universe = new ActiveSymbolUniverse();
        var snapshots = new List<UniverseSnapshot>();
        universe.BasketUpdated += s => snapshots.Add(s);

        universe.SetFromBasket("HQQQ", "fp-1", new DateOnly(2026, 4, 18),
            new[] { "AAPL" }, "fallback-seed", DateTimeOffset.UtcNow);
        universe.SetFromBasket("HQQQ", "fp-2", new DateOnly(2026, 4, 18),
            new[] { "AAPL", "MSFT" }, "fallback-seed", DateTimeOffset.UtcNow);

        Assert.Equal(2, snapshots.Count);
        Assert.Equal("fp-2", snapshots[1].Fingerprint);
    }
}
