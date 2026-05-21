using Hqqq.Domain.ValueObjects;
using Hqqq.QuoteEngine.Persistence;
using Hqqq.QuoteEngine.Services;
using Hqqq.QuoteEngine.State;
using Hqqq.QuoteEngine.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Hqqq.QuoteEngine.Tests.Services;

/// <summary>
/// Direct coverage for <see cref="BootstrapCalibrationCoordinator"/>.
/// The coordinator is the seam that anchors per-share NAV to a live
/// QQQ price; these tests assert the four observable behaviours
/// expected by the long-term hardening plan:
/// <list type="number">
///   <item>First QQQ tick after activation calibrates from anchor / rawValue.</item>
///   <item>Subsequent ticks (or a duplicate QQQ tick) do not re-calibrate.</item>
///   <item>A previously persisted record restores scale verbatim on activation.</item>
///   <item>Missing QQQ / unpriced basket / non-anchor ticks do not flip state.</item>
/// </list>
/// </summary>
public class BootstrapCalibrationCoordinatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 4, 16, 13, 30, 0, TimeSpan.Zero);

    private sealed record Rig(
        BootstrapCalibrationCoordinator Coordinator,
        BasketStateStore Baskets,
        PerSymbolQuoteStore Quotes,
        InMemoryCalibrationStore Store,
        FakeSystemClock Clock,
        QuoteEngineOptions Options);

    private static Rig BuildRig()
    {
        var clock = new FakeSystemClock(T0);
        var options = new QuoteEngineOptions
        {
            AnchorSymbol = "QQQ",
            StaleAfter = TimeSpan.FromSeconds(30),
        };
        var quotes = new PerSymbolQuoteStore(clock);
        var baskets = new BasketStateStore();
        var store = new InMemoryCalibrationStore();
        var coordinator = new BootstrapCalibrationCoordinator(
            baskets, quotes, options, store, clock,
            NullLogger<BootstrapCalibrationCoordinator>.Instance);

        return new Rig(coordinator, baskets, quotes, store, clock, options);
    }

    [Fact]
    public void OnBasketChanged_NoPriorRecord_DowngradesScaleToUninitialized()
    {
        var rig = BuildRig();

        var basket = new TestBasketBuilder()
            .WithBasketId("HQQQ")
            .WithFingerprint("fp-1")
            .WithScaleFactor(1m)            // reference-data placeholder
            .AddConstituent("AAPL", "Apple", 1000, 200m, 1.0m)
            .Build();
        rig.Baskets.Replace(basket);

        rig.Coordinator.OnBasketChanged();

        var current = rig.Baskets.Current;
        Assert.NotNull(current);
        Assert.False(current!.ScaleFactor.IsInitialized);
        Assert.Equal(0m, current.ScaleFactor.Value);
        Assert.Null(rig.Coordinator.CalibratedFingerprint);
    }

    [Fact]
    public void OnBasketChanged_PriorRecordMatches_RestoresPersistedScale()
    {
        var rig = BuildRig();

        rig.Store.Set(new CalibrationRecord
        {
            BasketId = "HQQQ",
            Fingerprint = "fp-1",
            ScaleFactor = 0.00125m,
            AnchorPrice = 550m,
            RawValue = 440_000m,
            CalibratedAtUtc = T0,
        });

        var basket = new TestBasketBuilder()
            .WithBasketId("HQQQ")
            .WithFingerprint("fp-1")
            .WithScaleFactor(1m)
            .AddConstituent("AAPL", "Apple", 1000, 200m, 1.0m)
            .Build();
        rig.Baskets.Replace(basket);

        rig.Coordinator.OnBasketChanged();

        var current = rig.Baskets.Current;
        Assert.NotNull(current);
        Assert.Equal(0.00125m, current!.ScaleFactor.Value);
        Assert.Equal("fp-1", rig.Coordinator.CalibratedFingerprint);
    }

    [Fact]
    public void OnBasketChanged_PriorRecordWrongFingerprint_TreatedAsMissing()
    {
        var rig = BuildRig();

        rig.Store.Set(new CalibrationRecord
        {
            BasketId = "HQQQ",
            Fingerprint = "fp-OLD",
            ScaleFactor = 0.0099m,
            AnchorPrice = 100m,
            RawValue = 10_100m,
            CalibratedAtUtc = T0,
        });

        var basket = new TestBasketBuilder()
            .WithBasketId("HQQQ")
            .WithFingerprint("fp-NEW")
            .WithScaleFactor(1m)
            .AddConstituent("AAPL", "Apple", 1000, 200m, 1.0m)
            .Build();
        rig.Baskets.Replace(basket);

        rig.Coordinator.OnBasketChanged();

        Assert.False(rig.Baskets.Current!.ScaleFactor.IsInitialized);
        Assert.Null(rig.Coordinator.CalibratedFingerprint);
    }

    [Fact]
    public void TryBootstrap_FirstAnchorTickWithCompleteBasket_CalibratesAndPersists()
    {
        var rig = BuildRig();

        // rawValue = 200*1000 + 400*500 + 1000*200 = 200000 + 200000 + 200000 = 600_000
        var basket = new TestBasketBuilder()
            .WithBasketId("HQQQ")
            .WithFingerprint("fp-1")
            .WithScaleFactor(1m)
            .AddConstituent("AAPL", "Apple",     1000, 200m,  0.333m)
            .AddConstituent("MSFT", "Microsoft",  500, 400m,  0.333m)
            .AddConstituent("NVDA", "NVIDIA",     200, 1000m, 0.334m)
            .Build();
        rig.Baskets.Replace(basket);
        rig.Coordinator.OnBasketChanged();

        rig.Quotes.Update(TestBasketBuilder.Tick("AAPL", 200m, rig.Clock.UtcNow));
        rig.Quotes.Update(TestBasketBuilder.Tick("MSFT", 400m, rig.Clock.UtcNow));
        rig.Quotes.Update(TestBasketBuilder.Tick("NVDA", 1000m, rig.Clock.UtcNow));
        rig.Quotes.Update(TestBasketBuilder.Tick("QQQ", 540m, rig.Clock.UtcNow));

        var fired = rig.Coordinator.TryBootstrap("QQQ");

        Assert.True(fired);
        var calibrated = rig.Baskets.Current!;
        Assert.True(calibrated.ScaleFactor.IsInitialized);
        // scale = 540 / 600_000 = 0.0009
        Assert.Equal(0.0009m, Math.Round(calibrated.ScaleFactor.Value, 6));
        // nav = scale × rawValue = 540 (the anchor price by construction)
        Assert.Equal(540m, Math.Round(calibrated.ScaleFactor.Value * 600_000m, 4));
        Assert.Equal("fp-1", rig.Coordinator.CalibratedFingerprint);

        // Persistence side effects
        Assert.Equal(1, rig.Store.WriteCount);
        var record = rig.Store.Peek("HQQQ");
        Assert.NotNull(record);
        Assert.Equal("fp-1", record!.Fingerprint);
        Assert.Equal(540m, record.AnchorPrice);
        Assert.Equal(600_000m, record.RawValue);
        Assert.Equal(T0, record.CalibratedAtUtc);
    }

    [Fact]
    public void TryBootstrap_AnchorTickProducesNavInRealisticRange()
    {
        // Smoke-check the plan's exit criterion: with a realistic
        // gross-basket-value (≈ $340B) and a typical QQQ price (≈ $550)
        // the bootstrap yields nav ≈ QQQ price, i.e. inside [100, 1000].
        var rig = BuildRig();

        var basket = new TestBasketBuilder()
            .WithBasketId("HQQQ")
            .WithFingerprint("fp-prod-like")
            .WithScaleFactor(1m)
            .AddConstituent("AAPL", "Apple", 1, 100m, 1.0m)
            .Build();

        rig.Baskets.Replace(basket);
        rig.Coordinator.OnBasketChanged();

        // Simulate a single anchor constituent with a gross value of ~$340B
        rig.Quotes.Update(TestBasketBuilder.Tick("AAPL", 340_000_000_000m, rig.Clock.UtcNow));
        rig.Quotes.Update(TestBasketBuilder.Tick("QQQ", 550m, rig.Clock.UtcNow));

        var fired = rig.Coordinator.TryBootstrap("QQQ");

        Assert.True(fired);
        var nav = rig.Baskets.Current!.ScaleFactor.Value * 340_000_000_000m;
        Assert.InRange(nav, 100m, 1000m);
    }

    [Fact]
    public void TryBootstrap_SecondAnchorTick_DoesNotReCalibrate()
    {
        var rig = BuildRig();

        var basket = new TestBasketBuilder()
            .WithBasketId("HQQQ")
            .WithFingerprint("fp-1")
            .WithScaleFactor(1m)
            .AddConstituent("AAPL", "Apple", 1000, 200m, 1.0m)
            .Build();
        rig.Baskets.Replace(basket);
        rig.Coordinator.OnBasketChanged();

        rig.Quotes.Update(TestBasketBuilder.Tick("AAPL", 200m, rig.Clock.UtcNow));
        rig.Quotes.Update(TestBasketBuilder.Tick("QQQ", 500m, rig.Clock.UtcNow));
        Assert.True(rig.Coordinator.TryBootstrap("QQQ"));
        var firstScale = rig.Baskets.Current!.ScaleFactor.Value;

        // A second QQQ tick at a different price must NOT re-calibrate.
        rig.Quotes.Update(TestBasketBuilder.Tick("QQQ", 600m, rig.Clock.UtcNow.AddSeconds(1)));
        var fired = rig.Coordinator.TryBootstrap("QQQ");

        Assert.False(fired);
        Assert.Equal(firstScale, rig.Baskets.Current!.ScaleFactor.Value);
        Assert.Equal(1, rig.Store.WriteCount);
    }

    [Fact]
    public void TryBootstrap_NonAnchorTick_ShortCircuits()
    {
        var rig = BuildRig();

        var basket = new TestBasketBuilder()
            .WithFingerprint("fp-1")
            .WithScaleFactor(1m)
            .AddConstituent("AAPL", "Apple", 1000, 200m, 1.0m)
            .Build();
        rig.Baskets.Replace(basket);
        rig.Coordinator.OnBasketChanged();

        rig.Quotes.Update(TestBasketBuilder.Tick("AAPL", 200m, rig.Clock.UtcNow));
        rig.Quotes.Update(TestBasketBuilder.Tick("QQQ", 500m, rig.Clock.UtcNow));

        // Even though the anchor price is in the store, a non-anchor
        // tick must not drive calibration.
        var fired = rig.Coordinator.TryBootstrap("AAPL");

        Assert.False(fired);
        Assert.False(rig.Baskets.Current!.ScaleFactor.IsInitialized);
    }

    [Fact]
    public void TryBootstrap_MissingAnchorTick_DoesNotCalibrate()
    {
        var rig = BuildRig();

        var basket = new TestBasketBuilder()
            .WithFingerprint("fp-1")
            .WithScaleFactor(1m)
            .AddConstituent("AAPL", "Apple", 1000, 200m, 1.0m)
            .Build();
        rig.Baskets.Replace(basket);
        rig.Coordinator.OnBasketChanged();

        rig.Quotes.Update(TestBasketBuilder.Tick("AAPL", 200m, rig.Clock.UtcNow));
        // No QQQ tick yet — bootstrap should refuse to fire.

        var fired = rig.Coordinator.TryBootstrap("QQQ");

        Assert.False(fired);
        Assert.False(rig.Baskets.Current!.ScaleFactor.IsInitialized);
        Assert.Equal(0, rig.Store.WriteCount);
    }

    [Fact]
    public void TryBootstrap_UnpricedBasket_DoesNotCalibrate()
    {
        var rig = BuildRig();

        var basket = new TestBasketBuilder()
            .WithFingerprint("fp-1")
            .WithScaleFactor(1m)
            .AddConstituent("AAPL", "Apple", 1000, 200m, 1.0m)
            .Build();
        rig.Baskets.Replace(basket);
        rig.Coordinator.OnBasketChanged();

        // Anchor available but no constituent ticks yet — rawValue == 0.
        rig.Quotes.Update(TestBasketBuilder.Tick("QQQ", 500m, rig.Clock.UtcNow));

        var fired = rig.Coordinator.TryBootstrap("QQQ");

        Assert.False(fired);
        Assert.False(rig.Baskets.Current!.ScaleFactor.IsInitialized);
    }

    [Fact]
    public void OnBasketChanged_TransitionToNewFingerprint_ResetsAndRequiresRecalibration()
    {
        var rig = BuildRig();

        var basketV1 = new TestBasketBuilder()
            .WithBasketId("HQQQ")
            .WithFingerprint("fp-1")
            .WithScaleFactor(1m)
            .AddConstituent("AAPL", "Apple", 1000, 200m, 1.0m)
            .Build();
        rig.Baskets.Replace(basketV1);
        rig.Coordinator.OnBasketChanged();
        rig.Quotes.Update(TestBasketBuilder.Tick("AAPL", 200m, rig.Clock.UtcNow));
        rig.Quotes.Update(TestBasketBuilder.Tick("QQQ", 500m, rig.Clock.UtcNow));
        Assert.True(rig.Coordinator.TryBootstrap("QQQ"));
        var v1Scale = rig.Baskets.Current!.ScaleFactor.Value;

        // Basket transition: new fingerprint resets coordinator state.
        var basketV2 = new TestBasketBuilder()
            .WithBasketId("HQQQ")
            .WithFingerprint("fp-2")
            .WithScaleFactor(1m)
            .AddConstituent("MSFT", "Microsoft", 500, 400m, 1.0m)
            .Build();
        rig.Baskets.Replace(basketV2);
        rig.Coordinator.OnBasketChanged();

        Assert.False(rig.Baskets.Current!.ScaleFactor.IsInitialized);
        Assert.Null(rig.Coordinator.CalibratedFingerprint);

        // First fp-2 anchor tick re-bootstraps independently.
        rig.Quotes.Update(TestBasketBuilder.Tick("MSFT", 400m, rig.Clock.UtcNow));
        rig.Quotes.Update(TestBasketBuilder.Tick("QQQ", 450m, rig.Clock.UtcNow));
        Assert.True(rig.Coordinator.TryBootstrap("QQQ"));

        var v2Scale = rig.Baskets.Current!.ScaleFactor.Value;
        Assert.NotEqual(v1Scale, v2Scale);
        Assert.Equal("fp-2", rig.Coordinator.CalibratedFingerprint);
        Assert.Equal(2, rig.Store.WriteCount);
    }

    [Fact]
    public void OnBasketChanged_AlreadyCalibratedBasket_PreservesPersistedScaleAcrossRestart()
    {
        // Simulates a process restart: the in-memory coordinator has no
        // prior state, but Redis carries the calibration from the
        // previous process. Activation should restore the scale verbatim
        // without waiting for a QQQ tick.
        var rig = BuildRig();
        rig.Store.Set(new CalibrationRecord
        {
            BasketId = "HQQQ",
            Fingerprint = "fp-restart",
            ScaleFactor = 0.000_002_5m,
            AnchorPrice = 500m,
            RawValue = 200_000_000m,
            CalibratedAtUtc = T0.AddHours(-4),
        });
        var baselineWrites = rig.Store.WriteCount;

        var basket = new TestBasketBuilder()
            .WithBasketId("HQQQ")
            .WithFingerprint("fp-restart")
            .WithScaleFactor(1m)
            .AddConstituent("AAPL", "Apple", 1000, 200m, 1.0m)
            .Build();
        rig.Baskets.Replace(basket);

        rig.Coordinator.OnBasketChanged();

        Assert.Equal(0.000_002_5m, rig.Baskets.Current!.ScaleFactor.Value);
        Assert.Equal("fp-restart", rig.Coordinator.CalibratedFingerprint);
        // No new write — restore is read-only.
        Assert.Equal(baselineWrites, rig.Store.WriteCount);

        // And subsequent ticks do not re-calibrate.
        rig.Quotes.Update(TestBasketBuilder.Tick("AAPL", 200m, rig.Clock.UtcNow));
        rig.Quotes.Update(TestBasketBuilder.Tick("QQQ", 999m, rig.Clock.UtcNow));
        Assert.False(rig.Coordinator.TryBootstrap("QQQ"));
        Assert.Equal(0.000_002_5m, rig.Baskets.Current!.ScaleFactor.Value);
    }

    [Fact]
    public void OnBasketChanged_PersistedRecordHasNonPositiveScale_TreatedAsMissing()
    {
        // Defensive guard: a corrupt record with scale=0 must not slip
        // through and silently keep the engine off-air. The coordinator
        // should fall through to fresh bootstrap.
        var rig = BuildRig();
        rig.Store.Set(new CalibrationRecord
        {
            BasketId = "HQQQ",
            Fingerprint = "fp-1",
            ScaleFactor = 0m,
            AnchorPrice = 500m,
            RawValue = 100_000m,
            CalibratedAtUtc = T0,
        });

        var basket = new TestBasketBuilder()
            .WithBasketId("HQQQ")
            .WithFingerprint("fp-1")
            .WithScaleFactor(1m)
            .AddConstituent("AAPL", "Apple", 1000, 200m, 1.0m)
            .Build();
        rig.Baskets.Replace(basket);

        rig.Coordinator.OnBasketChanged();

        Assert.False(rig.Baskets.Current!.ScaleFactor.IsInitialized);
        Assert.Null(rig.Coordinator.CalibratedFingerprint);
    }

    [Fact]
    public void OnBasketChanged_NoBasket_NoOp()
    {
        var rig = BuildRig();

        rig.Coordinator.OnBasketChanged();

        Assert.Null(rig.Baskets.Current);
        Assert.Null(rig.Coordinator.CalibratedFingerprint);
    }

    [Fact]
    public void TryBootstrap_NoBasket_NoOp()
    {
        var rig = BuildRig();

        Assert.False(rig.Coordinator.TryBootstrap("QQQ"));
        Assert.Null(rig.Coordinator.CalibratedFingerprint);
    }
}
