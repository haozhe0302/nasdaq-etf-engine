using System.Net;
using System.Net.Http;
using System.Text.Json;
using Hqqq.Gateway.Tests.Fixtures;

namespace Hqqq.Gateway.Tests;

public class UpstreamFailureTests : IDisposable
{
    private readonly FakeHttpMessageHandler _fakeHandler;
    private readonly GatewayAppFactory _factory;
    private readonly HttpClient _client;

    public UpstreamFailureTests()
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
    public async Task UpstreamFailure_Returns502_WithControlledPayload()
    {
        _fakeHandler.SetHandler((_, _) =>
            throw new HttpRequestException("Connection refused"));

        var response = await _client.GetAsync("/api/quote");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("upstream_unavailable", doc.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task UpstreamFailure_DoesNotPoison_SubsequentRequests()
    {
        _fakeHandler.SetHandler((_, _) =>
            throw new HttpRequestException("Connection refused"));

        var failResponse = await _client.GetAsync("/api/quote");
        Assert.Equal(HttpStatusCode.BadGateway, failResponse.StatusCode);

        _fakeHandler.SetResponse(HttpStatusCode.OK, """{"nav":100}""");

        var okResponse = await _client.GetAsync("/api/quote");
        Assert.Equal(HttpStatusCode.OK, okResponse.StatusCode);
    }

    [Fact]
    public async Task UpstreamFailure_SystemHealth_Returns502()
    {
        _fakeHandler.SetHandler((_, _) =>
            throw new HttpRequestException("Connection refused"));

        var response = await _client.GetAsync("/api/system/health");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        Assert.Equal("upstream_unavailable", doc.RootElement.GetProperty("status").GetString());
    }
}
