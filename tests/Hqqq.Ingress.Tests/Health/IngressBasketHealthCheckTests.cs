using Hqqq.Contracts.Events;
using Hqqq.Ingress.Clients;
using Hqqq.Ingress.Configuration;
using Hqqq.Ingress.Health;
using Hqqq.Ingress.State;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hqqq.Ingress.Tests.Health;

public class IngressBasketHealthCheckTests
{
    [Fact]
    public async Task NoBasketAndNoOverride_IsUnhealthy()
    {
        var check = BuildCheck(out var universe, out var coordinator, out _);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("no basket received", result.Description);
    }

    [Fact]
    public async Task BootstrapOverride_IsDegraded()
    {
        var check = BuildCheck(out var universe, out var coordinator, out _);
        coordinator.SeedBootstrapSymbols(new[] { "AAPL" });

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        Assert.Contains("bootstrap override", result.Description);
    }

    [Fact]
    public async Task BasketApplied_IsHealthy_AndExposesMetadata()
    {
        var check = BuildCheck(out var universe, out var coordinator, out _);

        var snap = new UniverseSnapshot
        {
            BasketId = "HQQQ",
            Fingerprint = "fp-123",
            AsOfDate = new DateOnly(2026, 4, 18),
            Symbols = new HashSet<string>(new[] { "AAPL", "MSFT" }, StringComparer.Ordinal),
            Source = "fallback-seed",
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        universe.SetFromBasket(snap.BasketId, snap.Fingerprint, snap.AsOfDate,
            snap.Symbols, snap.Source, snap.UpdatedAtUtc);
        await coordinator.ApplyAsync(snap, CancellationToken.None);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("HQQQ", result.Data["basketId"]);
        Assert.Equal("fp-123", result.Data["basketFingerprint"]);
        Assert.Equal(2, (int)result.Data["basketConstituentCount"]);
        Assert.Equal(2, (int)result.Data["appliedSymbolCount"]);
    }

    private static IngressBasketHealthCheck BuildCheck(
        out ActiveSymbolUniverse universe,
        out BasketSubscriptionCoordinator coordinator,
        out StubClient client)
    {
        universe = new ActiveSymbolUniverse();
        client = new StubClient();
        coordinator = new BasketSubscriptionCoordinator(
            universe, client, NullLogger<BasketSubscriptionCoordinator>.Instance);
        return new IngressBasketHealthCheck(
            universe, coordinator, Options.Create(new IngressBasketOptions()));
    }

    internal sealed class StubClient : ITiingoStreamClient
    {
        public bool IsConnected => false;
        public DateTimeOffset? LastDataUtc => null;
        public Task ConnectAndStreamAsync(IEnumerable<string> symbols,
            Func<RawTickV1, CancellationToken, Task> onTick, CancellationToken ct) => Task.CompletedTask;
        public Task SubscribeAsync(IEnumerable<string> symbols, CancellationToken ct) => Task.CompletedTask;
        public Task UnsubscribeAsync(IEnumerable<string> symbols, CancellationToken ct) => Task.CompletedTask;
    }
}
