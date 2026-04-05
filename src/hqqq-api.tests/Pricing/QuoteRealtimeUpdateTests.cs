using System.Text.Json;
using Hqqq.Api.Modules.Pricing.Contracts;

namespace Hqqq.Api.Tests.Pricing;

public class QuoteRealtimeUpdateTests
{
    private static QuoteSnapshot MakeSnapshot(IReadOnlyList<SeriesPoint>? series = null)
    {
        return new QuoteSnapshot
        {
            Nav = 123.4567m,
            NavChangePct = 0.1234m,
            MarketPrice = 456.78m,
            PremiumDiscountPct = 0.0567m,
            Qqq = 456.78m,
            BasketValueB = 1.2345m,
            AsOf = new DateTimeOffset(2026, 4, 5, 14, 30, 0, TimeSpan.Zero),
            Series = series ?? GenerateSeries(500),
            Movers =
            [
                new Mover
                {
                    Symbol = "AAPL",
                    Name = "Apple Inc",
                    ChangePct = 1.5m,
                    Impact = 12.3m,
                    Direction = "up",
                },
            ],
            Freshness = new FreshnessInfo
            {
                SymbolsTotal = 100,
                SymbolsFresh = 95,
                SymbolsStale = 5,
                FreshPct = 95m,
                LastTickUtc = DateTimeOffset.UtcNow,
                AvgTickIntervalMs = 250,
            },
            Feeds = new FeedInfo
            {
                WebSocketConnected = true,
                FallbackActive = false,
                PricingActive = true,
                BasketState = "active",
                PendingActivationBlocked = false,
            },
        };
    }

    private static List<SeriesPoint> GenerateSeries(int count)
    {
        var list = new List<SeriesPoint>(count);
        var baseTime = new DateTimeOffset(2026, 4, 5, 9, 30, 0, TimeSpan.FromHours(-4));
        for (int i = 0; i < count; i++)
        {
            list.Add(new SeriesPoint
            {
                Time = baseTime.AddSeconds(i * 15),
                Nav = 100m + i * 0.01m,
                Market = 100.5m + i * 0.01m,
            });
        }
        return list;
    }

    [Fact]
    public void FromSnapshot_DoesNotIncludeSeriesArray()
    {
        var snapshot = MakeSnapshot(GenerateSeries(500));
        var update = QuoteRealtimeUpdate.FromSnapshot(snapshot, latestSeriesPoint: null);

        var json = JsonSerializer.Serialize(update);
        Assert.DoesNotContain("\"series\"", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromSnapshot_PreservesScalarFields()
    {
        var snapshot = MakeSnapshot();
        var update = QuoteRealtimeUpdate.FromSnapshot(snapshot, latestSeriesPoint: null);

        Assert.Equal(snapshot.Nav, update.Nav);
        Assert.Equal(snapshot.NavChangePct, update.NavChangePct);
        Assert.Equal(snapshot.MarketPrice, update.MarketPrice);
        Assert.Equal(snapshot.PremiumDiscountPct, update.PremiumDiscountPct);
        Assert.Equal(snapshot.Qqq, update.Qqq);
        Assert.Equal(snapshot.BasketValueB, update.BasketValueB);
        Assert.Equal(snapshot.AsOf, update.AsOf);
    }

    [Fact]
    public void FromSnapshot_IncludesLatestSeriesPointWhenProvided()
    {
        var snapshot = MakeSnapshot();
        var point = new SeriesPoint
        {
            Time = DateTimeOffset.UtcNow,
            Nav = 123.45m,
            Market = 456.78m,
        };
        var update = QuoteRealtimeUpdate.FromSnapshot(snapshot, point);

        Assert.NotNull(update.LatestSeriesPoint);
        Assert.Equal(point.Nav, update.LatestSeriesPoint.Nav);
        Assert.Equal(point.Market, update.LatestSeriesPoint.Market);
        Assert.Equal(point.Time, update.LatestSeriesPoint.Time);
    }

    [Fact]
    public void FromSnapshot_LatestSeriesPointIsNullWhenNoneRecorded()
    {
        var snapshot = MakeSnapshot();
        var update = QuoteRealtimeUpdate.FromSnapshot(snapshot, latestSeriesPoint: null);

        Assert.Null(update.LatestSeriesPoint);
    }

    [Fact]
    public void FromSnapshot_PreservesFreshnessAndFeeds()
    {
        var snapshot = MakeSnapshot();
        var update = QuoteRealtimeUpdate.FromSnapshot(snapshot, latestSeriesPoint: null);

        Assert.Equal(snapshot.Freshness.SymbolsTotal, update.Freshness.SymbolsTotal);
        Assert.Equal(snapshot.Freshness.SymbolsStale, update.Freshness.SymbolsStale);
        Assert.Equal(snapshot.Feeds.WebSocketConnected, update.Feeds.WebSocketConnected);
        Assert.Equal(snapshot.Feeds.PricingActive, update.Feeds.PricingActive);
    }

    [Fact]
    public void FromSnapshot_PreserversMovers()
    {
        var snapshot = MakeSnapshot();
        var update = QuoteRealtimeUpdate.FromSnapshot(snapshot, latestSeriesPoint: null);

        Assert.Equal(snapshot.Movers.Count, update.Movers.Count);
        Assert.Equal("AAPL", update.Movers[0].Symbol);
    }

    [Fact]
    public void RealtimeUpdate_IsDramaticallySmallerThanFullSnapshot()
    {
        var series = GenerateSeries(500);
        var snapshot = MakeSnapshot(series);
        var update = QuoteRealtimeUpdate.FromSnapshot(snapshot, latestSeriesPoint: null);

        var snapshotJson = JsonSerializer.Serialize(snapshot);
        var updateJson = JsonSerializer.Serialize(update);

        Assert.True(
            updateJson.Length < snapshotJson.Length / 3,
            $"Delta ({updateJson.Length} chars) should be <33% of full snapshot ({snapshotJson.Length} chars)");
    }

    [Fact]
    public void RealtimeUpdate_WithLatestPoint_StillMuchSmaller()
    {
        var series = GenerateSeries(500);
        var snapshot = MakeSnapshot(series);
        var point = new SeriesPoint
        {
            Time = DateTimeOffset.UtcNow,
            Nav = 123.45m,
            Market = 456.78m,
        };
        var update = QuoteRealtimeUpdate.FromSnapshot(snapshot, point);

        var snapshotJson = JsonSerializer.Serialize(snapshot);
        var updateJson = JsonSerializer.Serialize(update);

        Assert.True(
            updateJson.Length < snapshotJson.Length / 3,
            $"Delta with point ({updateJson.Length} chars) should be <33% of full snapshot ({snapshotJson.Length} chars)");
    }
}
