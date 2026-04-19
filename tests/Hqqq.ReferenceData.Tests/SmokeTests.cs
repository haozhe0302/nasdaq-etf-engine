using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Hqqq.ReferenceData.Tests;

public class SmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public SmokeTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task LiveHealthCheck_ReturnsOk()
    {
        var response = await _client.GetAsync("/healthz/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\": \"healthy\"", body);
        Assert.Contains("\"serviceName\": \"hqqq-reference-data\"", body);
    }

    [Fact]
    public async Task ReadyHealthCheck_ReturnsOk()
    {
        var response = await _client.GetAsync("/healthz/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MetricsEndpoint_IsExposed()
    {
        var response = await _client.GetAsync("/metrics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("# EOF", body);
    }

    [Fact]
    public async Task GetCurrentBasket_ReturnsOk()
    {
        var response = await _client.GetAsync("/api/basket/current");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("HQQQ", body);
    }
}
