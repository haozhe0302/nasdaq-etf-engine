using System.Net;
using Hqqq.ReferenceData.Publishing;
using Hqqq.ReferenceData.Services;
using Hqqq.ReferenceData.Sources;
using Hqqq.ReferenceData.Tests.TestDoubles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Hqqq.ReferenceData.Tests.Health;

/// <summary>
/// Pins the HTTP status-code contract on <c>/healthz/ready</c>:
/// <list type="bullet">
///   <item><c>Healthy</c> → 200</item>
///   <item><c>Degraded</c> → 503</item>
///   <item><c>Unhealthy</c> → 503</item>
/// </list>
/// This is the "Degraded must not silently return 200" guarantee.
/// ASP.NET's default <see cref="HealthCheckOptions"/> maps <c>Degraded</c>
/// to 200; Program.cs overrides that for this service via
/// <c>MapHqqqHealthEndpoints(readyStatusCodes)</c>, and this test would
/// catch any regression to the default.
/// </summary>
public class ReadinessStatusCodeTests : IClassFixture<ReadinessStatusCodeTests.IsolatedReadinessFactory>
{
    private readonly IsolatedReadinessFactory _factory;

    public ReadinessStatusCodeTests(IsolatedReadinessFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Ready_ReturnsExpectedStatusCodes_AcrossAllThreeStates()
    {
        var client = _factory.CreateClient();

        // Wait for startup refresh to populate the store (Healthy branch).
        await WaitForReady(client, HttpStatusCode.OK);

        // Force Degraded: one consecutive publish failure (threshold=1 in
        // test config). The store already has a basket + a successful
        // publish (from the startup refresh that the CapturingPublisher
        // accepted), so we just record a failure directly.
        _factory.PublishHealth.RecordAttempt(DateTimeOffset.UtcNow);
        _factory.PublishHealth.RecordFailure(DateTimeOffset.UtcNow, "simulated broker blip");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/healthz/ready")).StatusCode);

        // Force Unhealthy: enough additional failures to pass the
        // UnhealthyAfterConsecutiveFailures threshold=2.
        _factory.PublishHealth.RecordFailure(DateTimeOffset.UtcNow, "simulated broker blip");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/healthz/ready")).StatusCode);

        // Recovery back to Healthy.
        _factory.PublishHealth.RecordSuccess(DateTimeOffset.UtcNow, _factory.GetCurrentFingerprint()!);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz/ready")).StatusCode);
    }

    private static async Task WaitForReady(HttpClient client, HttpStatusCode expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var r = await client.GetAsync("/healthz/ready");
            if (r.StatusCode == expected) return;
            await Task.Delay(100);
        }
        Assert.Fail($"/healthz/ready did not reach {expected} within the startup budget");
    }

    public sealed class IsolatedReadinessFactory : WebApplicationFactory<Program>
    {
        public CapturingPublisher Publisher { get; } = new();
        public PublishHealthTracker PublishHealth { get; private set; } = null!;
        public ActiveBasketStore Store { get; private set; } = null!;

        public string? GetCurrentFingerprint() => Store.Current?.Fingerprint;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Seed mode keeps the startup refresh deterministic —
            // CapturingPublisher always sees a valid basket and the
            // PublishHealth state machine reaches Healthy predictably.
            builder.UseSetting("ReferenceData:Basket:Mode", "Seed");

            builder.ConfigureServices(services =>
            {
                // Swap the Kafka publisher so the startup refresh actually
                // "publishes" and the Healthy branch triggers.
                var publishers = services
                    .Where(d => d.ServiceType == typeof(IBasketPublisher))
                    .ToList();
                foreach (var d in publishers) services.Remove(d);
                services.AddSingleton<IBasketPublisher>(Publisher);

                // Narrow /healthz/ready to just the active-basket check so
                // the Kafka health probe doesn't drag the whole report to
                // Unhealthy in the test host.
                services.PostConfigure<HealthCheckServiceOptions>(opts =>
                {
                    var keep = opts.Registrations
                        .Where(r => r.Name == "active-basket" || r.Tags.Contains("live"))
                        .ToList();
                    opts.Registrations.Clear();
                    foreach (var r in keep) opts.Registrations.Add(r);
                });

                // Tighten thresholds so Degraded and Unhealthy are both
                // reachable without sleeping.
                services.PostConfigure<Hqqq.ReferenceData.Configuration.ReferenceDataOptions>(opts =>
                {
                    opts.PublishHealth = new Hqqq.ReferenceData.Configuration.PublishHealthOptions
                    {
                        FirstActivationGraceSeconds = 60,
                        DegradedAfterConsecutiveFailures = 1,
                        UnhealthyAfterConsecutiveFailures = 2,
                        MaxSilenceSeconds = 3600,
                    };
                });
            });
        }

        protected override void ConfigureClient(HttpClient client)
        {
            base.ConfigureClient(client);
            // Snapshot the DI-resolved singletons so tests can manipulate them.
            PublishHealth = Services.GetRequiredService<PublishHealthTracker>();
            Store = Services.GetRequiredService<ActiveBasketStore>();
        }
    }
}
