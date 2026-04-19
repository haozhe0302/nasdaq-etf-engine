using System.Net;
using System.Net.Http;
using System.Text.Json;
using Hqqq.Observability.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hqqq.QuoteEngine.Tests;

/// <summary>
/// Phase 2D1 — verifies that the worker-side management surface used by
/// hqqq-quote-engine exposes the standard Phase 2 endpoints. The actual
/// pricing pipeline isn't booted; only the observability registration
/// shared with Program.cs.
/// </summary>
public class HealthzAndMetricsExposedTests
{
    [Fact]
    public async Task ManagementHost_ExposesHealthzAndMetrics_ForQuoteEngineService()
    {
        var hostBuilder = Host.CreateApplicationBuilder();
        hostBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Management:Enabled"] = "true",
            ["Management:Port"] = "0",
            ["Management:BindAddress"] = "127.0.0.1",
        });

        hostBuilder.Services.AddHqqqObservability("hqqq-quote-engine", hostBuilder.Environment);
        hostBuilder.Services.AddHqqqManagementHost(hostBuilder.Configuration);

        using var host = hostBuilder.Build();
        await host.StartAsync();
        try
        {
            var mgmt = host.Services.GetRequiredService<ManagementHost>();
            Assert.True(mgmt.BoundPort.HasValue);

            using var http = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{mgmt.BoundPort!.Value}"),
                Timeout = TimeSpan.FromSeconds(5),
            };

            var live = await http.GetAsync("/healthz/live");
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            using var liveDoc = JsonDocument.Parse(await live.Content.ReadAsStringAsync());
            Assert.Equal("hqqq-quote-engine",
                liveDoc.RootElement.GetProperty("serviceName").GetString());
            Assert.Equal("healthy", liveDoc.RootElement.GetProperty("status").GetString());

            var ready = await http.GetAsync("/healthz/ready");
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

            var metrics = await http.GetAsync("/metrics");
            Assert.Equal(HttpStatusCode.OK, metrics.StatusCode);
            Assert.Contains("# EOF", await metrics.Content.ReadAsStringAsync());
        }
        finally
        {
            await host.StopAsync();
        }
    }
}
