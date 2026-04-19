using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Hqqq.Gateway.Tests;

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
        Assert.Contains("\"serviceName\": \"hqqq-gateway\"", body);
    }

    [Fact]
    public async Task ReadyHealthCheck_ReturnsOk()
    {
        var response = await _client.GetAsync("/healthz/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MetricsEndpoint_IsExposed_OnGateway()
    {
        var response = await _client.GetAsync("/metrics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        // Prometheus exporter always terminates with "# EOF" even when no
        // measurements have been recorded yet — that's enough to assert
        // /metrics is wired and reachable.
        Assert.Contains("# EOF", body);
    }

    [Fact]
    public async Task QuoteEndpoint_ReturnsOk_InStubMode()
    {
        var response = await _client.GetAsync("/api/quote");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("nav", body);
    }
}
