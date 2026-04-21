using System.Net;
using System.Text.Json;
using Hqqq.ReferenceData.Publishing;
using Hqqq.ReferenceData.Tests.TestDoubles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Hqqq.ReferenceData.Tests;

/// <summary>
/// End-to-end verification that starting the host with default configuration
/// (LiveHoldings=None) produces an active basket from the committed
/// fallback seed and publishes it to Kafka without any operator action.
/// The startup-refresh path is exactly what the README promises for the
/// interview-grade demo posture, and this test pins it.
/// </summary>
public class StartupEndToEndTests : IClassFixture<StartupEndToEndTests.Factory>
{
    private readonly Factory _factory;

    public StartupEndToEndTests(Factory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task OnStartup_FallbackSeedActivatesAndPublishes()
    {
        var client = _factory.CreateClient();

        // The startup refresh kicks off inside BasketRefreshJob.ExecuteAsync
        // asynchronously, so poll /current until it flips from 503 → 200.
        HttpResponseMessage? last = null;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            last = await client.GetAsync("/api/basket/current");
            if (last.StatusCode == HttpStatusCode.OK) break;
            await Task.Delay(100);
        }

        Assert.NotNull(last);
        Assert.Equal(HttpStatusCode.OK, last!.StatusCode);

        using var doc = JsonDocument.Parse(await last.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        Assert.Equal("HQQQ", root.GetProperty("active").GetProperty("basketId").GetString());
        Assert.Equal("fallback-seed", root.GetProperty("source").GetString());

        var constituents = root.GetProperty("constituents");
        Assert.True(constituents.GetArrayLength() >= 90,
            $"expected ~100 constituents from the committed fallback seed, got {constituents.GetArrayLength()}");

        var publishStatus = root.GetProperty("publishStatus");
        Assert.Equal("healthy", publishStatus.GetProperty("state").GetString());
        Assert.True(publishStatus.GetProperty("currentFingerprintPublished").GetBoolean());

        Assert.NotEmpty(_factory.Publisher.Published);
        var ev = _factory.Publisher.Published[0];
        Assert.Equal("fallback-seed", ev.Source);
        Assert.True(ev.ConstituentCount >= 90);

        // Phase-2-native corp-action layer: the wire event must always
        // carry an AdjustmentSummary (even when empty on a fresh seed) so
        // downstream consumers can rely on the field's presence.
        Assert.NotNull(ev.AdjustmentSummary);
        Assert.Equal(0, ev.AdjustmentSummary!.SplitsApplied);
        Assert.Equal(0, ev.AdjustmentSummary.RenamesApplied);
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public CapturingPublisher Publisher { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                var existing = services
                    .Where(d => d.ServiceType == typeof(IBasketPublisher))
                    .ToList();
                foreach (var d in existing) services.Remove(d);

                services.AddSingleton<IBasketPublisher>(Publisher);
            });
        }
    }
}
