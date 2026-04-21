using System.Net;
using System.Text.Json;
using Hqqq.ReferenceData.Publishing;
using Hqqq.ReferenceData.Services;
using Hqqq.ReferenceData.Tests.TestDoubles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Hqqq.ReferenceData.Tests.Endpoints;

/// <summary>
/// End-to-end HTTP tests that wire the REAL pipeline
/// (<see cref="CompositeHoldingsSource"/> → <see cref="FallbackSeedHoldingsSource"/>
/// → <see cref="BasketRefreshPipeline"/> → <see cref="BasketService"/>)
/// and only swap out <see cref="IBasketPublisher"/> for a
/// <see cref="CapturingPublisher"/>. Contrast with
/// <see cref="BasketEndpointsTests"/> which stubs <see cref="IBasketService"/>
/// wholesale — this one catches regressions those stubbed tests cannot
/// (projection, publish-status surfacing, full constituent payload in
/// the HTTP response, etc.).
/// </summary>
public class BasketEndpointsIntegrationTests : IClassFixture<BasketEndpointsIntegrationTests.RealPipelineFactory>
{
    private readonly RealPipelineFactory _factory;

    public BasketEndpointsIntegrationTests(RealPipelineFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RefreshThenCurrent_WithFallbackSeed_ReturnsFullBasketAndHealthyPublishStatus()
    {
        var client = _factory.CreateClient();

        var refresh1 = await client.PostAsync("/api/basket/refresh", content: null);
        Assert.Equal(HttpStatusCode.OK, refresh1.StatusCode);
        using (var doc = JsonDocument.Parse(await refresh1.Content.ReadAsStringAsync()))
        {
            // Depending on whether BasketRefreshJob startup refresh ran first,
            // the REST-triggered refresh may be "changed" (first-ever) or
            // "unchanged" (no-op second run). Both are success.
            var status = doc.RootElement.GetProperty("status").GetString();
            Assert.Contains(status, new[] { "changed", "unchanged" });
            Assert.Equal("fallback-seed", doc.RootElement.GetProperty("source").GetString());
        }

        var refresh2 = await client.PostAsync("/api/basket/refresh", content: null);
        Assert.Equal(HttpStatusCode.OK, refresh2.StatusCode);
        using (var doc = JsonDocument.Parse(await refresh2.Content.ReadAsStringAsync()))
        {
            // Second call always unchanged (same seed, same fingerprint).
            Assert.Equal("unchanged", doc.RootElement.GetProperty("status").GetString());
            Assert.False(doc.RootElement.GetProperty("changed").GetBoolean());
        }

        var current = await client.GetAsync("/api/basket/current");
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);

        using var currentDoc = JsonDocument.Parse(await current.Content.ReadAsStringAsync());
        var root = currentDoc.RootElement;

        Assert.Equal("HQQQ", root.GetProperty("active").GetProperty("basketId").GetString());
        Assert.Equal("fallback-seed", root.GetProperty("source").GetString());

        var constituents = root.GetProperty("constituents");
        Assert.True(constituents.GetArrayLength() >= 90,
            $"expected ~100 constituents from the committed seed, got {constituents.GetArrayLength()}");

        var publishStatus = root.GetProperty("publishStatus");
        Assert.Equal("healthy", publishStatus.GetProperty("state").GetString());
        Assert.True(publishStatus.GetProperty("currentFingerprintPublished").GetBoolean(),
            "current fingerprint must be reflected as published after a successful publish");
        Assert.Equal(0, publishStatus.GetProperty("consecutivePublishFailures").GetInt32());

        Assert.NotEmpty(_factory.Publisher.Published);
        Assert.Equal("HQQQ", _factory.Publisher.Published[0].BasketId);
    }

    public sealed class RealPipelineFactory : WebApplicationFactory<Program>
    {
        public CapturingPublisher Publisher { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // This test asserts `source == "fallback-seed"` so the
            // real-source four-source pipeline must stay dormant. Pin
            // the service to Seed mode; the composite degrades to
            // "live (file/http) → fallback-seed" exactly as before.
            builder.UseSetting("ReferenceData:Basket:Mode", "Seed");

            builder.ConfigureServices(services =>
            {
                // Only swap the Kafka publisher. Everything else — sources,
                // validator, pipeline, service, refresh job, endpoints —
                // stays real.
                var existing = services
                    .Where(d => d.ServiceType == typeof(IBasketPublisher))
                    .ToList();
                foreach (var d in existing) services.Remove(d);

                services.AddSingleton<IBasketPublisher>(Publisher);
            });
        }
    }
}
