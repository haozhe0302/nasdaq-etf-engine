using System.Net;
using System.Text.Json;
using Hqqq.Gateway.Services.Timescale;
using Hqqq.Gateway.Tests.Fixtures;
using Npgsql;

namespace Hqqq.Gateway.Tests;

/// <summary>
/// Locks the Phase 2C2 contract for <c>/api/history</c> served directly
/// from Timescale via <c>Gateway:Sources:History=timescale</c>:
/// <list type="bullet">
///   <item><description>Response shape still matches the frontend adapter <c>BHistoryResponse</c> (top-level + <c>series[time,nav,marketPrice]</c>).</description></item>
///   <item><description>Supported ranges map to the expected UTC windows.</description></item>
///   <item><description>Unsupported ranges return a controlled 400.</description></item>
///   <item><description>Empty windows return a render-safe 200 payload.</description></item>
///   <item><description>Query failures return a controlled 503 without silently falling back to stub.</description></item>
/// </list>
/// </summary>
public class TimescaleHistoryEndpointTests
{
    private const string TestBasketId = "HQQQ-TEST";

    private static readonly DateTimeOffset Anchor =
        new(2026, 4, 17, 14, 30, 0, TimeSpan.Zero);

    private static (GatewayAppFactory factory, HttpClient client, FakeTimescaleHistoryQueryService query)
        BuildTimescale(Action<FakeTimescaleHistoryQueryService>? configure = null)
    {
        var query = new FakeTimescaleHistoryQueryService();
        configure?.Invoke(query);

        var factory = new GatewayAppFactory()
            .WithConfig("Gateway:DataSource", "stub")
            .WithConfig("Gateway:BasketId", TestBasketId)
            .WithConfig("Gateway:Sources:History", "timescale")
            .WithFakeHistoryQuery(query);

        return (factory, factory.CreateClient(), query);
    }

    // ── Contract shape lock-in ─────────────────────────

    [Fact]
    public async Task History_Timescale_PreservesFrontendContract()
    {
        var rows = new List<HistoryRow>
        {
            new(Anchor.AddMinutes(-10), 99.80m, 99.75m),
            new(Anchor.AddMinutes(-5), 99.90m, 99.85m),
            new(Anchor, 100.00m, 99.95m),
        };
        var (factory, client, _) = BuildTimescale(q => q.SetRows(rows));
        using var _f = factory; using var _c = client;

        var response = await client.GetAsync("/api/history?range=1D");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;

        Assert.Equal("1D", root.GetProperty("range").GetString());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("startDate").GetString()));
        Assert.False(string.IsNullOrEmpty(root.GetProperty("endDate").GetString()));
        Assert.Equal(3, root.GetProperty("pointCount").GetInt32());
        Assert.Equal(3, root.GetProperty("totalPoints").GetInt32());
        Assert.True(root.TryGetProperty("isPartial", out _));

        var series = root.GetProperty("series");
        Assert.Equal(3, series.GetArrayLength());
        var first = series[0];
        Assert.True(first.TryGetProperty("time", out _));
        Assert.Equal(99.80m, first.GetProperty("nav").GetDecimal());
        Assert.Equal(99.75m, first.GetProperty("marketPrice").GetDecimal());

        var tracking = root.GetProperty("trackingError");
        foreach (var key in new[] { "rmseBps", "maxAbsBasisBps", "avgAbsBasisBps", "maxDeviationPct", "correlation" })
            Assert.True(tracking.TryGetProperty(key, out _), $"missing trackingError.{key}");

        var dist = root.GetProperty("distribution");
        Assert.Equal(21, dist.GetArrayLength());
        Assert.Equal("-10", dist[0].GetProperty("label").GetString());
        Assert.Equal("10", dist[20].GetProperty("label").GetString());

        var diag = root.GetProperty("diagnostics");
        Assert.Equal(3, diag.GetProperty("snapshots").GetInt32());
        Assert.True(diag.TryGetProperty("gaps", out _));
        Assert.True(diag.TryGetProperty("completenessPct", out _));
        Assert.True(diag.TryGetProperty("daysLoaded", out _));
    }

    [Fact]
    public async Task History_Timescale_UsesConfiguredBasketId()
    {
        var (factory, client, query) = BuildTimescale(q => q.SetRows(new List<HistoryRow>
        {
            new(Anchor, 100m, 99.9m),
        }));
        using var _f = factory; using var _c = client;

        var response = await client.GetAsync("/api/history?range=1D");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(query.Calls);
        Assert.Equal(TestBasketId, query.Calls[0].BasketId);
    }

    // ── Range mapping ──────────────────────────────────

    [Theory]
    [InlineData("1D")]
    [InlineData("5D")]
    [InlineData("1M")]
    [InlineData("3M")]
    [InlineData("YTD")]
    [InlineData("1Y")]
    public async Task History_Timescale_SupportedRange_Returns200(string range)
    {
        var (factory, client, query) = BuildTimescale(q => q.SetRows(Array.Empty<HistoryRow>()));
        using var _f = factory; using var _c = client;

        var response = await client.GetAsync($"/api/history?range={range}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(query.Calls);

        var call = query.Calls[0];
        // From must never be after To.
        Assert.True(call.FromUtc <= call.ToUtc);

        // Confirm the windows roughly match the legacy mapping.
        var from = call.FromUtc.UtcDateTime;
        var to = call.ToUtc.UtcDateTime;
        var days = (to.Date - from.Date).Days;
        switch (range)
        {
            case "1D":
                Assert.Equal(0, days);
                break;
            case "5D":
                Assert.Equal(4, days);
                break;
            case "1M":
                Assert.True(days >= 28 && days <= 31);
                break;
            case "3M":
                Assert.True(days >= 88 && days <= 92);
                break;
            case "YTD":
                Assert.Equal(1, from.Month);
                Assert.Equal(1, from.Day);
                break;
            case "1Y":
                Assert.True(days >= 364 && days <= 366);
                break;
        }
    }

    [Fact]
    public async Task History_Timescale_UnsupportedRange_Returns400()
    {
        var (factory, client, query) = BuildTimescale();
        using var _f = factory; using var _c = client;

        var response = await client.GetAsync("/api/history?range=2Y");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("history_range_unsupported", doc.RootElement.GetProperty("error").GetString());
        Assert.Equal("2Y", doc.RootElement.GetProperty("range").GetString());
        Assert.True(doc.RootElement.GetProperty("supported").GetArrayLength() > 0);

        // Query service must not be invoked for an invalid range.
        Assert.Empty(query.Calls);
    }

    [Fact]
    public async Task History_Timescale_MissingRange_Returns400()
    {
        var (factory, client, query) = BuildTimescale();
        using var _f = factory; using var _c = client;

        var response = await client.GetAsync("/api/history");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(query.Calls);
    }

    // ── Empty window ───────────────────────────────────

    [Fact]
    public async Task History_Timescale_EmptyWindow_Returns200WithRenderSafePayload()
    {
        var (factory, client, _) = BuildTimescale(q => q.SetRows(Array.Empty<HistoryRow>()));
        using var _f = factory; using var _c = client;

        var response = await client.GetAsync("/api/history?range=1D");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;

        Assert.Equal(0, root.GetProperty("pointCount").GetInt32());
        Assert.Equal(0, root.GetProperty("totalPoints").GetInt32());
        Assert.Equal(0, root.GetProperty("series").GetArrayLength());
        Assert.Equal(21, root.GetProperty("distribution").GetArrayLength());

        var diag = root.GetProperty("diagnostics");
        Assert.Equal(0, diag.GetProperty("snapshots").GetInt32());
        Assert.Equal(0, diag.GetProperty("gaps").GetInt32());
        Assert.Equal(0, diag.GetProperty("daysLoaded").GetInt32());
    }

    // ── Failure handling ───────────────────────────────

    [Fact]
    public async Task History_Timescale_QueryFailure_Returns503_NoStubFallback()
    {
        var (factory, client, _) = BuildTimescale(q =>
            q.ThrowOnLoad(new NpgsqlException("simulated transport failure")));
        using var _f = factory; using var _c = client;

        var response = await client.GetAsync("/api/history?range=1D");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("history_unavailable", doc.RootElement.GetProperty("error").GetString());
        // The 503 body intentionally does NOT contain the stub-shaped
        // `series`/`trackingError`/`distribution` fields — silent fallback
        // to stub when `timescale` was requested is forbidden.
        Assert.False(doc.RootElement.TryGetProperty("series", out _));
    }

    [Fact]
    public async Task History_Timescale_UnexpectedFailure_Returns503()
    {
        var (factory, client, _) = BuildTimescale(q =>
            q.ThrowOnLoad(new InvalidOperationException("boom")));
        using var _f = factory; using var _c = client;

        var response = await client.GetAsync("/api/history?range=1D");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("history_unavailable", doc.RootElement.GetProperty("error").GetString());
    }
}
