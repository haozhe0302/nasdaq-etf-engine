using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Hqqq.ReferenceData.Tests;

public class SmokeTests : IClassFixture<SmokeTests.SeedModeFactory>
{
    private readonly HttpClient _client;

    public SmokeTests(SeedModeFactory factory)
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
    public async Task ReadyHealthCheck_Responds()
    {
        // Smoke-level: ensure the /healthz/ready endpoint is wired and
        // returns a well-formed status. It may legitimately be
        // ServiceUnavailable in-process because there is no real Kafka
        // broker to publish the active basket against — the state
        // machine correctly reports "degraded / unhealthy" until the
        // first publish lands. The deployment smoke (see
        // infra/azure/scripts/phase2-azure-smoke.sh) asserts strict
        // readiness against a real broker.
        var response = await _client.GetAsync("/healthz/ready");
        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable,
            $"/healthz/ready returned unexpected status {(int)response.StatusCode}");
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

    /// <summary>
    /// Pins the in-process smoke to <c>BasketMode=Seed</c> so the
    /// four-source RealSource pipeline does not fire live HTTP against
    /// Nasdaq / AlphaVantage / the anchor scrapers during startup.
    /// </summary>
    public sealed class SeedModeFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ReferenceData:Basket:Mode", "Seed");
        }
    }
}
