using System.Net;
using System.Net.Http;
using System.Text.Json;
using Hqqq.Observability.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hqqq.Observability.Tests.Hosting;

/// <summary>
/// Boots a worker-style generic host with <see cref="ManagementHost"/>
/// bound to an OS-chosen port and verifies that the embedded management
/// surface exposes the standard endpoints. Asserts the contract that
/// downstream services rely on:
/// <list type="bullet">
///   <item><c>/healthz/live</c> returns 200 with the standard payload.</item>
///   <item><c>/healthz/ready</c> returns 200 (no ready checks configured).</item>
///   <item><c>/metrics</c> returns a Prometheus-formatted body.</item>
/// </list>
/// </summary>
public class ManagementHostTests
{
    [Fact]
    public async Task ManagementHost_ExposesHealthzAndMetrics_OnEmbeddedPort()
    {
        var hostBuilder = Host.CreateApplicationBuilder();
        // Port=0 lets Kestrel pick a free port — avoids collisions when the
        // suite runs in parallel with running services.
        hostBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Management:Enabled"] = "true",
            ["Management:Port"] = "0",
            ["Management:BindAddress"] = "127.0.0.1",
        });
        hostBuilder.Services.AddHqqqObservability("hqqq-test-worker", hostBuilder.Environment);
        hostBuilder.Services.AddHqqqManagementHost(hostBuilder.Configuration);

        using var host = hostBuilder.Build();
        await host.StartAsync();

        try
        {
            var mgmt = host.Services.GetRequiredService<ManagementHost>();
            Assert.True(mgmt.BoundPort.HasValue && mgmt.BoundPort.Value > 0,
                "ManagementHost did not surface a bound port");

            using var client = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{mgmt.BoundPort!.Value}"),
                Timeout = TimeSpan.FromSeconds(5),
            };

            var live = await client.GetAsync("/healthz/live");
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);
            var liveBody = await live.Content.ReadAsStringAsync();
            using (var doc = JsonDocument.Parse(liveBody))
            {
                var root = doc.RootElement;
                Assert.Equal("hqqq-test-worker", root.GetProperty("serviceName").GetString());
                Assert.Equal("healthy", root.GetProperty("status").GetString());
            }

            var ready = await client.GetAsync("/healthz/ready");
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

            var metrics = await client.GetAsync("/metrics");
            Assert.Equal(HttpStatusCode.OK, metrics.StatusCode);
            var metricsBody = await metrics.Content.ReadAsStringAsync();
            Assert.Contains("# EOF", metricsBody);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task ManagementHost_Disabled_DoesNotBindAnyPort()
    {
        var hostBuilder = Host.CreateApplicationBuilder();
        hostBuilder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Management:Enabled"] = "false",
            ["Management:Port"] = "0",
        });
        hostBuilder.Services.AddHqqqObservability("hqqq-disabled-worker", hostBuilder.Environment);
        hostBuilder.Services.AddHqqqManagementHost(hostBuilder.Configuration);

        using var host = hostBuilder.Build();
        await host.StartAsync();
        try
        {
            var mgmt = host.Services.GetRequiredService<ManagementHost>();
            Assert.Null(mgmt.BoundPort);
        }
        finally
        {
            await host.StopAsync();
        }
    }
}
