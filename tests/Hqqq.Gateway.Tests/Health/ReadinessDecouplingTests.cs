using System.Linq;
using Hqqq.Observability.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Hqqq.Gateway.Tests.Health;

/// <summary>
/// Pins the "a TimescaleDB outage must not take the gateway out of ingress
/// rotation" contract at the health-check registration level.
///
/// The ACA readiness probe targets <c>/healthz/ready</c>, which runs every
/// registered check that is NOT tagged
/// <see cref="ObservabilityRegistration.AggregateOnlyTag"/>. The gateway
/// serves <c>/api/quote</c> and <c>/api/constituents</c> from Redis and only
/// <c>/api/history</c> depends on Timescale, so the Timescale probe must be
/// aggregate-only (visible in <c>/api/system/health</c>) and must NOT carry
/// the ready tag — otherwise a stopped database black-holes the whole edge,
/// which is exactly the production incident this guards against. Redis, by
/// contrast, IS core to the edge and must stay on the readiness gate.
/// </summary>
public class ReadinessDecouplingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ReadinessDecouplingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Timescale_IsAggregateOnly_AndOffTheReadinessGate()
    {
        var registrations = _factory.Services
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations;

        var timescale = registrations.SingleOrDefault(r => r.Name == "timescale");
        Assert.NotNull(timescale);
        Assert.Contains(ObservabilityRegistration.AggregateOnlyTag, timescale!.Tags);
        Assert.DoesNotContain(ObservabilityRegistration.ReadyTag, timescale.Tags);
    }

    [Fact]
    public void Redis_StaysOnTheReadinessGate()
    {
        var registrations = _factory.Services
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations;

        var redis = registrations.SingleOrDefault(r => r.Name == "redis");
        Assert.NotNull(redis);
        Assert.Contains(ObservabilityRegistration.ReadyTag, redis!.Tags);
        Assert.DoesNotContain(ObservabilityRegistration.AggregateOnlyTag, redis.Tags);
    }
}
