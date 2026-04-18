using System.Net;
using System.Text.Json;
using Hqqq.Gateway.Tests.Fixtures;

namespace Hqqq.Gateway.Tests;

public class LegacyForwardingTests : IDisposable
{
    private readonly FakeHttpMessageHandler _fakeHandler;
    private readonly GatewayAppFactory _factory;
    private readonly HttpClient _client;

    public LegacyForwardingTests()
    {
        _fakeHandler = new FakeHttpMessageHandler();
        _factory = new GatewayAppFactory()
            .WithConfig("Gateway:DataSource", "legacy")
            .WithConfig("Gateway:LegacyBaseUrl", "http://legacy.test")
            .WithFakeHandler(_fakeHandler);
        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task Quote_ForwardsToCorrectPath()
    {
        _fakeHandler.SetResponse(HttpStatusCode.OK, """{"nav":100}""");
        var response = await _client.GetAsync("/api/quote");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_fakeHandler.Requests);
        Assert.Equal("/api/quote", _fakeHandler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Constituents_ForwardsToCorrectPath()
    {
        _fakeHandler.SetResponse(HttpStatusCode.OK, """{"holdings":[]}""");
        var response = await _client.GetAsync("/api/constituents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_fakeHandler.Requests);
        Assert.Equal("/api/constituents", _fakeHandler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task History_PreservesRangeQueryString()
    {
        _fakeHandler.SetResponse(HttpStatusCode.OK, """{"range":"5D","series":[]}""");
        var response = await _client.GetAsync("/api/history?range=5D");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_fakeHandler.Requests);

        var uri = _fakeHandler.Requests[0].RequestUri!;
        Assert.Equal("/api/history", uri.AbsolutePath);
        Assert.Contains("range=5D", uri.Query);
    }

    [Fact]
    public async Task Passthrough_PreservesUpstreamStatusAndBody()
    {
        var upstreamBody = """{"status":"initializing","message":"not ready"}""";
        _fakeHandler.SetResponse(HttpStatusCode.ServiceUnavailable, upstreamBody);

        var response = await _client.GetAsync("/api/quote");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("initializing", body);
    }

    [Fact]
    public async Task SystemHealth_OverlaysGatewayMetadata()
    {
        var upstreamHealth = """
        {
            "serviceName": "hqqq-api",
            "status": "healthy",
            "checkedAtUtc": "2025-01-02T14:30:00+00:00",
            "version": "1.0.0",
            "dependencies": [],
            "runtime": { "uptimeSeconds": 100 }
        }
        """;
        _fakeHandler.SetResponse(HttpStatusCode.OK, upstreamHealth);

        var response = await _client.GetAsync("/api/system/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;

        Assert.Equal("hqqq-gateway", root.GetProperty("serviceName").GetString());
        Assert.Equal("legacy", root.GetProperty("sourceMode").GetString());
        Assert.Equal("healthy", root.GetProperty("status").GetString());
        Assert.Equal("1.0.0", root.GetProperty("version").GetString());

        var upstream = root.GetProperty("upstream");
        Assert.Equal("hqqq-api", upstream.GetProperty("serviceName").GetString());
    }
}
