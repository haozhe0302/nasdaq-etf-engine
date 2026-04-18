using System.Net;
using System.Text.Json;
using Hqqq.Contracts.Dtos;
using Hqqq.Gateway.Services.Sources;
using Hqqq.Gateway.Tests.Fixtures;
using Hqqq.Infrastructure.Redis;
using Hqqq.Infrastructure.Serialization;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Hqqq.Gateway.Tests;

/// <summary>
/// Covers the B5 cut-over of /api/quote and /api/constituents to the
/// Redis-backed sources: mode selection, happy path, missing-key degraded
/// response, malformed-payload degraded response, transport-failure handling,
/// and guarantees that history/system-health stay on the transitional path.
/// </summary>
public class RedisModeEndpointTests
{
    private const string TestBasketId = "HQQQ-TEST";

    private static readonly DateTimeOffset QuoteAsOf =
        new(2026, 4, 17, 14, 30, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset ConstituentsAsOf =
        new(2026, 4, 17, 14, 29, 0, TimeSpan.Zero);

    private static QuoteSnapshotDto BuildQuoteSnapshot() => new()
    {
        Nav = 101.25m,
        NavChangePct = 0.42m,
        MarketPrice = 101.10m,
        PremiumDiscountPct = -0.15m,
        Qqq = 451.00m,
        QqqChangePct = 0.31m,
        BasketValueB = 1.8m,
        AsOf = QuoteAsOf,
        Series = new List<SeriesPointDto>
        {
            new() { Time = QuoteAsOf.AddMinutes(-5), Nav = 101.00m, Market = 100.90m },
            new() { Time = QuoteAsOf, Nav = 101.25m, Market = 101.10m },
        },
        Movers = new List<MoverDto>
        {
            new() { Symbol = "NVDA", Name = "NVIDIA Corp.", ChangePct = 2.10m, Impact = 18.0m, Direction = "up" },
        },
        Freshness = new FreshnessDto
        {
            SymbolsTotal = 100,
            SymbolsFresh = 99,
            SymbolsStale = 1,
            FreshPct = 99.0m,
            LastTickUtc = QuoteAsOf,
            AvgTickIntervalMs = 220.0,
        },
        Feeds = new FeedInfoDto
        {
            WebSocketConnected = true,
            FallbackActive = false,
            PricingActive = true,
            BasketState = "active",
            PendingActivationBlocked = false,
        },
        QuoteState = "live",
        IsLive = true,
        IsFrozen = false,
    };

    private static ConstituentsSnapshotDto BuildConstituentsSnapshot() => new()
    {
        Holdings = new List<ConstituentHoldingDto>
        {
            new()
            {
                Symbol = "AAPL",
                Name = "Apple Inc.",
                Sector = "Technology",
                Weight = 12.5m,
                Shares = 1000m,
                Price = 185.00m,
                ChangePct = 1.20m,
                MarketValue = 185000m,
                SharesOrigin = "official",
                IsStale = false,
            },
        },
        Concentration = new ConcentrationDto
        {
            Top5Pct = 40m,
            Top10Pct = 55m,
            Top20Pct = 72m,
            SectorCount = 8,
            HerfindahlIndex = 0.05m,
        },
        Quality = new BasketQualityDto
        {
            TotalSymbols = 100,
            OfficialSharesCount = 100,
            DerivedSharesCount = 0,
            PricedCount = 100,
            StaleCount = 0,
            PriceCoveragePct = 100m,
            BasketMode = "official",
        },
        Source = new BasketSourceDto
        {
            AnchorSource = "nasdaq",
            TailSource = "alpha-vantage",
            BasketMode = "official",
            IsDegraded = false,
            AsOfDate = "2026-04-17",
            Fingerprint = "fp-test-0001",
        },
        AsOf = ConstituentsAsOf,
    };

    private static GatewayAppFactory BuildFactory(
        FakeGatewayRedisReader reader,
        bool constituentsRedis = true)
    {
        var factory = new GatewayAppFactory()
            .WithConfig("Gateway:DataSource", "stub")
            .WithConfig("Gateway:Sources:Quote", "redis")
            .WithConfig("Gateway:BasketId", TestBasketId)
            .WithFakeRedisReader(reader);

        if (constituentsRedis)
            factory.WithConfig("Gateway:Sources:Constituents", "redis");

        return factory;
    }

    [Fact]
    public async Task Quote_RedisMode_ReturnsLatestSnapshot_FromRedis()
    {
        var reader = new FakeGatewayRedisReader();
        var snapshot = BuildQuoteSnapshot();
        reader.Set(
            RedisKeys.Snapshot(TestBasketId),
            JsonSerializer.Serialize(snapshot, HqqqJsonDefaults.Options));

        using var factory = BuildFactory(reader);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/quote");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;

        Assert.Equal(101.25m, root.GetProperty("nav").GetDecimal());
        Assert.Equal("live", root.GetProperty("quoteState").GetString());
        Assert.True(root.GetProperty("isLive").GetBoolean());
        Assert.True(root.GetProperty("series").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Constituents_RedisMode_ReturnsLatestConstituents_FromRedis()
    {
        var reader = new FakeGatewayRedisReader();
        var snapshot = BuildConstituentsSnapshot();
        reader.Set(
            RedisKeys.Constituents(TestBasketId),
            JsonSerializer.Serialize(snapshot, HqqqJsonDefaults.Options));

        using var factory = BuildFactory(reader);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/constituents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;

        Assert.True(root.GetProperty("holdings").GetArrayLength() > 0);
        var first = root.GetProperty("holdings")[0];
        Assert.Equal("AAPL", first.GetProperty("symbol").GetString());
        Assert.Equal("fp-test-0001", root.GetProperty("source").GetProperty("fingerprint").GetString());
    }

    [Fact]
    public async Task Quote_RedisMode_MissingKey_Returns503_WithErrorBody()
    {
        var reader = new FakeGatewayRedisReader();
        using var factory = BuildFactory(reader);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/quote");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;
        Assert.Equal("quote_unavailable", root.GetProperty("error").GetString());
        Assert.Equal(TestBasketId, root.GetProperty("basketId").GetString());
    }

    [Fact]
    public async Task Constituents_RedisMode_MissingKey_Returns503_WithErrorBody()
    {
        var reader = new FakeGatewayRedisReader();
        using var factory = BuildFactory(reader);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/constituents");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;
        Assert.Equal("constituents_unavailable", root.GetProperty("error").GetString());
        Assert.Equal(TestBasketId, root.GetProperty("basketId").GetString());
    }

    [Fact]
    public async Task Quote_RedisMode_MalformedPayload_Returns502()
    {
        var reader = new FakeGatewayRedisReader()
            .Set(RedisKeys.Snapshot(TestBasketId), "{not json");

        using var factory = BuildFactory(reader);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/quote");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("quote_malformed", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Constituents_RedisMode_MalformedPayload_Returns502()
    {
        var reader = new FakeGatewayRedisReader()
            .Set(RedisKeys.Constituents(TestBasketId), "not-valid-json-at-all");

        using var factory = BuildFactory(reader);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/constituents");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("constituents_malformed", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Quote_RedisMode_TransportError_Returns503()
    {
        var reader = new FakeGatewayRedisReader()
            .Throw(RedisKeys.Snapshot(TestBasketId), new RedisConnectionException(ConnectionFailureType.UnableToConnect, "boom"));

        using var factory = BuildFactory(reader);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/quote");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("quote_redis_error", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task RedisMode_DoesNotForceHistoryOrHealthToRedis()
    {
        // Only quote is overridden to redis; constituents inherits global=stub,
        // history + health stay on global=stub. Neither should attempt Redis.
        var reader = new FakeGatewayRedisReader();

        using var factory = new GatewayAppFactory()
            .WithConfig("Gateway:DataSource", "stub")
            .WithConfig("Gateway:Sources:Quote", "redis")
            .WithConfig("Gateway:BasketId", TestBasketId)
            .WithFakeRedisReader(reader);

        using var client = factory.CreateClient();

        var historyResponse = await client.GetAsync("/api/history?range=1D");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);

        var healthResponse = await client.GetAsync("/api/system/health");
        Assert.Equal(HttpStatusCode.OK, healthResponse.StatusCode);

        using var healthDoc = await JsonDocument.ParseAsync(await healthResponse.Content.ReadAsStreamAsync());
        Assert.Equal("stub", healthDoc.RootElement.GetProperty("sourceMode").GetString());

        var constituentsResponse = await client.GetAsync("/api/constituents");
        Assert.Equal(HttpStatusCode.OK, constituentsResponse.StatusCode);
        using var constituentsDoc = await JsonDocument.ParseAsync(
            await constituentsResponse.Content.ReadAsStreamAsync());
        Assert.Equal(
            "AAPL",
            constituentsDoc.RootElement.GetProperty("holdings")[0].GetProperty("symbol").GetString());
    }

    [Fact]
    public async Task Routes_AreUnchanged_InRedisMode()
    {
        var reader = new FakeGatewayRedisReader();
        reader.Set(
            RedisKeys.Snapshot(TestBasketId),
            JsonSerializer.Serialize(BuildQuoteSnapshot(), HqqqJsonDefaults.Options));
        reader.Set(
            RedisKeys.Constituents(TestBasketId),
            JsonSerializer.Serialize(BuildConstituentsSnapshot(), HqqqJsonDefaults.Options));

        using var factory = BuildFactory(reader);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/quote")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/constituents")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/basket/constituents")).StatusCode);

        // SignalR hub negotiate endpoint lives at {hub}/negotiate. A POST with
        // no body exercises only the route match — we don't need a full
        // protocol handshake to assert the hub is mapped.
        using var negotiate = new HttpRequestMessage(HttpMethod.Post, "/hubs/market/negotiate");
        var negotiateResponse = await client.SendAsync(negotiate);
        Assert.NotEqual(HttpStatusCode.NotFound, negotiateResponse.StatusCode);
    }

    [Fact]
    public void QuoteSource_IsRedisQuoteSource_WhenRedisModeSelected()
    {
        var reader = new FakeGatewayRedisReader();
        using var factory = BuildFactory(reader);

        using var scope = factory.Services.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<IQuoteSource>();
        Assert.IsType<RedisQuoteSource>(source);
    }

    [Fact]
    public void ConstituentsSource_IsRedisConstituentsSource_WhenRedisModeSelected()
    {
        var reader = new FakeGatewayRedisReader();
        using var factory = BuildFactory(reader);

        using var scope = factory.Services.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<IConstituentsSource>();
        Assert.IsType<RedisConstituentsSource>(source);
    }
}
