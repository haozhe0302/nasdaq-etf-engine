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
    public async Task ReadyHealthCheck_Responds()
    {
        // Smoke-level: ensure /healthz/ready is wired. It may legitimately
        // report 503 in-process because Redis/Timescale are unreachable
        // from the test host — the health checks intentionally surface
        // that condition. The deployment smoke (see
        // infra/azure/scripts/phase2-azure-smoke.sh) asserts strict
        // readiness against real infrastructure.
        var response = await _client.GetAsync("/healthz/ready");
        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable,
            $"/healthz/ready returned unexpected status {(int)response.StatusCode}");
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
