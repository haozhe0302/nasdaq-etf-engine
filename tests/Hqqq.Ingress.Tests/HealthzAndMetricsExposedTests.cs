using System.Net;
using System.Net.Http;
using System.Text.Json;
using Hqqq.Observability.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hqqq.Ingress.Tests;

/// <summary>
/// Phase 2D1 — verifies the worker-side management surface used by
/// hqqq-ingress: <c>/healthz/live</c>, <c>/healthz/ready</c>, and
/// <c>/metrics</c> are reachable on the embedded ManagementHost. Mirrors
/// the registration in Program.cs but avoids booting Tiingo/Kafka so the
/// test stays focused on the observability contract.
/// </summary>
public class HealthzAndMetricsExposedTests
{
    [Fact]
    public async Task ManagementHost_ExposesHealthzAndMetrics_ForIngressService()
    {
        var hostBuilder = Host.CreateApplicationBuilder();
        hostBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Management:Enabled"] = "true",
            ["Management:Port"] = "0",
            ["Management:BindAddress"] = "127.0.0.1",
        });

        hostBuilder.Services.AddHqqqObservability("hqqq-ingress", hostBuilder.Environment);
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
            Assert.Equal("hqqq-ingress", liveDoc.RootElement.GetProperty("serviceName").GetString());
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
