using System.Net;
using System.Text.Json;
using Hqqq.Contracts.Dtos;
using Hqqq.Gateway.Configuration;
using Hqqq.Gateway.Services.Adapters.Legacy;
using Hqqq.Gateway.Services.Sources;
using Hqqq.Gateway.Tests.Fixtures;
using Hqqq.Infrastructure.Redis;
using Hqqq.Infrastructure.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Hqqq.Gateway.Tests;

/// <summary>
/// Locks the Phase 2 B5 mixed-mode precedence contract:
/// <c>Gateway:DataSource=legacy</c> (global fallback) combined with
/// <c>Gateway:Sources:Quote=redis</c> and <c>Gateway:Sources:Constituents=redis</c>
/// per-endpoint overrides must resolve so that
/// <list type="bullet">
///   <item><description><c>/api/quote</c> and <c>/api/constituents</c> read from Redis.</description></item>
///   <item><description><c>/api/history</c> and <c>/api/system/health</c> continue to forward to the legacy HTTP upstream.</description></item>
/// </list>
/// No source-resolution logic is expected to change; this test is a
/// lock-in for the current behavior and catches any future drift.
/// </summary>
public class MixedModePrecedenceTests : IDisposable
{
    private const string TestBasketId = "HQQQ-TEST";

    private static readonly DateTimeOffset QuoteAsOf =
        new(2026, 4, 17, 14, 30, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset ConstituentsAsOf =
        new(2026, 4, 17, 14, 29, 0, TimeSpan.Zero);

    private readonly FakeGatewayRedisReader _redis;
    private readonly FakeHttpMessageHandler _http;
    private readonly GatewayAppFactory _factory;
    private readonly HttpClient _client;

    public MixedModePrecedenceTests()
    {
        _redis = new FakeGatewayRedisReader();
        _redis.Set(
            RedisKeys.Snapshot(TestBasketId),
            JsonSerializer.Serialize(BuildQuoteSnapshot(), HqqqJsonDefaults.Options));
        _redis.Set(
            RedisKeys.Constituents(TestBasketId),
            JsonSerializer.Serialize(BuildConstituentsSnapshot(), HqqqJsonDefaults.Options));

        _http = new FakeHttpMessageHandler();

        _factory = new GatewayAppFactory()
            .WithConfig("Gateway:DataSource", "legacy")
            .WithConfig("Gateway:LegacyBaseUrl", "http://legacy.test")
            .WithConfig("Gateway:BasketId", TestBasketId)
            .WithConfig("Gateway:Sources:Quote", "redis")
            .WithConfig("Gateway:Sources:Constituents", "redis")
            .WithFakeRedisReader(_redis)
            .WithFakeHandler(_http);

        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public void ResolvedSourceModes_ReflectMixedPrecedence()
    {
        using var scope = _factory.Services.CreateScope();
        var modes = scope.ServiceProvider.GetRequiredService<ResolvedSourceModes>();

        Assert.Equal(GatewayDataSourceMode.Redis, modes.Quote);
        Assert.Equal(GatewayDataSourceMode.Redis, modes.Constituents);
        Assert.Equal(GatewayDataSourceMode.Legacy, modes.History);
        Assert.Equal(GatewayDataSourceMode.Legacy, modes.SystemHealth);
    }

    [Fact]
    public void QuoteAndConstituentsSources_AreRedisBacked()
    {
        using var scope = _factory.Services.CreateScope();

        Assert.IsType<RedisQuoteSource>(
            scope.ServiceProvider.GetRequiredService<IQuoteSource>());
        Assert.IsType<RedisConstituentsSource>(
            scope.ServiceProvider.GetRequiredService<IConstituentsSource>());
    }

    [Fact]
    public void HistoryAndSystemHealthSources_StayOnLegacyForwarding()
    {
        using var scope = _factory.Services.CreateScope();

        Assert.IsType<LegacyHttpHistorySource>(
            scope.ServiceProvider.GetRequiredService<IHistorySource>());
        Assert.IsType<LegacyHttpSystemHealthSource>(
            scope.ServiceProvider.GetRequiredService<ISystemHealthSource>());
    }

    [Fact]
    public async Task Quote_ReadsFromRedis_WithoutTouchingLegacyHttp()
    {
        var response = await _client.GetAsync("/api/quote");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal(101.25m, doc.RootElement.GetProperty("nav").GetDecimal());

        // No legacy call should have been made for /api/quote.
        Assert.DoesNotContain(
            _http.Requests,
            r => r.RequestUri!.AbsolutePath == "/api/quote");
    }

    [Fact]
    public async Task Constituents_ReadsFromRedis_WithoutTouchingLegacyHttp()
    {
        var response = await _client.GetAsync("/api/constituents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal(
            "AAPL",
            doc.RootElement.GetProperty("holdings")[0].GetProperty("symbol").GetString());

        Assert.DoesNotContain(
            _http.Requests,
            r => r.RequestUri!.AbsolutePath == "/api/constituents");
    }

    [Fact]
    public async Task History_StillForwardsToLegacy_PreservingRangeQueryString()
    {
        _http.SetResponse(HttpStatusCode.OK, """{"range":"1D","series":[]}""");

        var response = await _client.GetAsync("/api/history?range=1D");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var historyCalls = _http.Requests
            .Where(r => r.RequestUri!.AbsolutePath == "/api/history")
            .ToList();
        Assert.Single(historyCalls);
        Assert.Contains("range=1D", historyCalls[0].RequestUri!.Query);
    }

    [Fact]
    public void StackedCutover_HistoryTimescale_OverridesLegacyGlobal()
    {
        // C2 scenario: quote via Redis, constituents via Legacy (inherited),
        // history via Timescale, system-health via Legacy (global fallback).
        using var factory = new GatewayAppFactory()
            .WithConfig("Gateway:DataSource", "legacy")
            .WithConfig("Gateway:LegacyBaseUrl", "http://legacy.test")
            .WithConfig("Gateway:BasketId", TestBasketId)
            .WithConfig("Gateway:Sources:Quote", "redis")
            .WithConfig("Gateway:Sources:History", "timescale")
            .WithFakeRedisReader(_redis)
            .WithFakeHandler(_http)
            .WithFakeHistoryQuery(new FakeTimescaleHistoryQueryService());

        using var _ = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var modes = scope.ServiceProvider.GetRequiredService<ResolvedSourceModes>();

        Assert.Equal(GatewayDataSourceMode.Redis, modes.Quote);
        Assert.Equal(GatewayDataSourceMode.Legacy, modes.Constituents);
        Assert.Equal(GatewayDataSourceMode.Timescale, modes.History);
        Assert.Equal(GatewayDataSourceMode.Legacy, modes.SystemHealth);

        Assert.IsType<RedisQuoteSource>(
            scope.ServiceProvider.GetRequiredService<IQuoteSource>());
        Assert.IsType<LegacyHttpConstituentsSource>(
            scope.ServiceProvider.GetRequiredService<IConstituentsSource>());
        Assert.IsType<TimescaleHistorySource>(
            scope.ServiceProvider.GetRequiredService<IHistorySource>());
        Assert.IsType<LegacyHttpSystemHealthSource>(
            scope.ServiceProvider.GetRequiredService<ISystemHealthSource>());
    }

    [Fact]
    public async Task SystemHealth_StillForwardsToLegacy_AndOverlaysGatewayMetadata()
    {
        var upstreamHealth = """
        {
            "serviceName": "hqqq-api",
            "status": "healthy",
            "checkedAtUtc": "2026-04-17T14:30:00+00:00",
            "version": "1.0.0",
            "dependencies": [],
            "runtime": { "uptimeSeconds": 100 }
        }
        """;
        _http.SetResponse(HttpStatusCode.OK, upstreamHealth);

        var response = await _client.GetAsync("/api/system/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;

        Assert.Equal("hqqq-gateway", root.GetProperty("serviceName").GetString());
        Assert.Equal("legacy", root.GetProperty("sourceMode").GetString());
        Assert.Equal("healthy", root.GetProperty("status").GetString());

        var healthCalls = _http.Requests
            .Where(r => r.RequestUri!.AbsolutePath == "/api/system/health")
            .ToList();
        Assert.Single(healthCalls);
    }

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
}
