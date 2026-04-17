using System.Net;
using System.Text.Json;
using Hqqq.Gateway.Tests.Fixtures;

namespace Hqqq.Gateway.Tests;

public class StubModeEndpointTests : IDisposable
{
    private readonly GatewayAppFactory _factory;
    private readonly HttpClient _client;

    public StubModeEndpointTests()
    {
        _factory = new GatewayAppFactory()
            .WithConfig("Gateway:DataSource", "stub");
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Quote_Returns200_WithDeterministicPayload()
    {
        var response = await _client.GetAsync("/api/quote");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;

        Assert.Equal(100.00m, root.GetProperty("nav").GetDecimal());
        Assert.Equal("stub", root.GetProperty("quoteState").GetString());
        Assert.False(root.GetProperty("isLive").GetBoolean());

        Assert.True(root.GetProperty("series").GetArrayLength() > 0);
        Assert.True(root.GetProperty("movers").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Constituents_Returns200_WithDeterministicPayload()
    {
        var response = await _client.GetAsync("/api/constituents");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;

        Assert.True(root.GetProperty("holdings").GetArrayLength() > 0);
        var first = root.GetProperty("holdings")[0];
        Assert.Equal("AAPL", first.GetProperty("symbol").GetString());
        Assert.True(first.GetProperty("weight").GetDecimal() > 0);
    }

    [Fact]
    public async Task History_Returns200_WithDeterministicPayload()
    {
        var response = await _client.GetAsync("/api/history?range=1D");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;

        Assert.Equal("1D", root.GetProperty("range").GetString());
        Assert.True(root.GetProperty("series").GetArrayLength() > 0);
        Assert.True(root.GetProperty("distribution").GetArrayLength() > 0);
        Assert.Equal(3, root.GetProperty("pointCount").GetInt32());
    }

    [Fact]
    public async Task History_DefaultRange_WhenNoQueryString()
    {
        var response = await _client.GetAsync("/api/history");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("1D", doc.RootElement.GetProperty("range").GetString());
    }

    [Fact]
    public async Task SystemHealth_Returns200_WithDeterministicPayload()
    {
        var response = await _client.GetAsync("/api/system/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;

        Assert.Equal("hqqq-gateway", root.GetProperty("serviceName").GetString());
        Assert.Equal("healthy", root.GetProperty("status").GetString());
        Assert.Equal("stub", root.GetProperty("sourceMode").GetString());
        Assert.True(root.GetProperty("dependencies").GetArrayLength() == 0);
    }
}
