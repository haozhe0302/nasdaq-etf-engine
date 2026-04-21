using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hqqq.Domain.Entities;
using Hqqq.Domain.ValueObjects;
using Hqqq.ReferenceData.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Hqqq.ReferenceData.Tests.Endpoints;

/// <summary>
/// Exercises <c>GET /api/basket/current</c> and <c>POST /api/basket/refresh</c>
/// end-to-end through <see cref="WebApplicationFactory{TEntryPoint}"/> with
/// a stubbed <see cref="IBasketService"/> so we can deterministically drive
/// empty / populated / changed / unchanged / error paths.
/// </summary>
public class BasketEndpointsTests : IClassFixture<BasketEndpointsTests.Factory>
{
    private readonly Factory _factory;

    public BasketEndpointsTests(Factory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetCurrent_WhenNoActiveBasket_Returns503()
    {
        _factory.Stub.Current = null;
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/basket/current");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("unavailable", body);
    }

    [Fact]
    public async Task GetCurrent_WhenPopulated_ReturnsFullBasket()
    {
        _factory.Stub.Current = BuildCurrent(source: "live:http", count: 100);
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/basket/current");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal("HQQQ", root.GetProperty("active").GetProperty("basketId").GetString());
        Assert.Equal(100, root.GetProperty("active").GetProperty("constituentCount").GetInt32());
        Assert.Equal("live:http", root.GetProperty("source").GetString());
        Assert.Equal(100, root.GetProperty("constituents").GetArrayLength());
    }

    [Fact]
    public async Task PostRefresh_OnChange_Returns200WithChangedTrue()
    {
        _factory.Stub.RefreshResult = new BasketRefreshResult
        {
            Success = true,
            Changed = true,
            Source = "live:file",
            Fingerprint = "fp-new",
            PreviousFingerprint = "fp-old",
            ConstituentCount = 101,
            AsOfDate = new DateOnly(2026, 4, 20),
        };
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/basket/refresh", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal("changed", root.GetProperty("status").GetString());
        Assert.True(root.GetProperty("changed").GetBoolean());
        Assert.Equal("live:file", root.GetProperty("source").GetString());
        Assert.Equal("fp-new", root.GetProperty("fingerprint").GetString());
        Assert.Equal("fp-old", root.GetProperty("previousFingerprint").GetString());
        Assert.Equal(101, root.GetProperty("constituentCount").GetInt32());
        Assert.Equal("2026-04-20", root.GetProperty("asOfDate").GetString());
    }

    [Fact]
    public async Task PostRefresh_WhenUnchanged_ReturnsOkWithChangedFalse()
    {
        _factory.Stub.RefreshResult = new BasketRefreshResult
        {
            Success = true,
            Changed = false,
            Source = "fallback-seed",
            Fingerprint = "fp-same",
            PreviousFingerprint = "fp-same",
            ConstituentCount = 100,
            AsOfDate = new DateOnly(2026, 4, 20),
        };
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/basket/refresh", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("unchanged", doc.RootElement.GetProperty("status").GetString());
        Assert.False(doc.RootElement.GetProperty("changed").GetBoolean());
    }

    [Fact]
    public async Task PostRefresh_OnFailure_Returns500()
    {
        _factory.Stub.RefreshResult = new BasketRefreshResult
        {
            Success = false,
            Error = "source unavailable",
        };
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/api/basket/refresh", content: null);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("error", doc.RootElement.GetProperty("status").GetString());
        Assert.Equal("source unavailable", doc.RootElement.GetProperty("error").GetString());
    }

    private static BasketCurrentResult BuildCurrent(string source, int count)
    {
        var active = new BasketVersion
        {
            BasketId = "HQQQ",
            VersionId = "v-endpoint-test",
            Fingerprint = new Fingerprint("fp"),
            AsOfDate = new DateOnly(2026, 4, 15),
            Status = BasketStatus.Active,
            ActivatedAtUtc = DateTimeOffset.UtcNow,
            ConstituentCount = count,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        var constituents = Enumerable.Range(0, count)
            .Select(i => new ConstituentWeight
            {
                Symbol = $"SYM{i:000}",
                SecurityName = $"Sec {i}",
                Sector = "Technology",
                Weight = 1m / count,
                SharesHeld = 100m + i,
                SharesOrigin = source,
            })
            .ToArray();

        return new BasketCurrentResult
        {
            Active = active,
            Constituents = constituents,
            Source = source,
            AsOfDate = active.AsOfDate,
            ActivatedAtUtc = active.ActivatedAtUtc!.Value,
            PublishStatus = new BasketPublishStatus
            {
                State = "healthy",
                CurrentFingerprintPublished = true,
            },
        };
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public StubBasketService Stub { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Seed mode keeps the background refresh job from firing
            // live HTTP against Nasdaq/AlphaVantage/scrapers at startup.
            // IBasketService is stubbed below so the HTTP endpoints
            // never touch the real pipeline anyway.
            builder.UseSetting("ReferenceData:Basket:Mode", "Seed");

            builder.ConfigureServices(services =>
            {
                // Override the real service (which depends on the background
                // refresh pipeline) with a deterministic stub.
                var existing = services
                    .Where(d => d.ServiceType == typeof(IBasketService))
                    .ToList();
                foreach (var d in existing) services.Remove(d);

                services.AddSingleton<IBasketService>(Stub);
            });
        }
    }

    public sealed class StubBasketService : IBasketService
    {
        public BasketCurrentResult? Current { get; set; }

        public BasketRefreshResult RefreshResult { get; set; } = new()
        {
            Success = true,
            Changed = false,
        };

        public Task<BasketCurrentResult?> GetCurrentAsync(CancellationToken ct = default)
            => Task.FromResult(Current);

        public Task<BasketRefreshResult> RefreshAsync(CancellationToken ct = default)
            => Task.FromResult(RefreshResult);
    }
}
