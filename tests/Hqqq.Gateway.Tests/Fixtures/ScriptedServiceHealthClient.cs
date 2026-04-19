using Hqqq.Gateway.Services.Adapters.Aggregated;

namespace Hqqq.Gateway.Tests.Fixtures;

/// <summary>
/// In-memory <see cref="IServiceHealthClient"/> for aggregator tests. Lets
/// each test script the snapshot per service name (or fall back to a
/// default), and records every probe so call ordering can be asserted.
/// </summary>
public sealed class ScriptedServiceHealthClient : IServiceHealthClient
{
    private readonly Dictionary<string, ServiceHealthSnapshot> _scripted = new(StringComparer.Ordinal);
    private readonly List<string> _probedServices = new();

    public IReadOnlyList<string> ProbedServices => _probedServices;

    public ScriptedServiceHealthClient SetSnapshot(string serviceName, ServiceHealthSnapshot snapshot)
    {
        _scripted[serviceName] = snapshot;
        return this;
    }

    public ScriptedServiceHealthClient SetHealthy(string serviceName, string version = "1.0.0")
        => SetSnapshot(serviceName, new ServiceHealthSnapshot
        {
            ServiceName = serviceName,
            Status = "healthy",
            Version = version,
            UptimeSeconds = 10,
            Dependencies = Array.Empty<ServiceHealthSnapshot.DependencyEntry>(),
            LastCheckedAtUtc = DateTimeOffset.UtcNow,
        });

    public ScriptedServiceHealthClient SetUnreachable(string serviceName, string error = "unreachable: connection refused")
        => SetSnapshot(serviceName, new ServiceHealthSnapshot
        {
            ServiceName = serviceName,
            Status = "unknown",
            Dependencies = Array.Empty<ServiceHealthSnapshot.DependencyEntry>(),
            LastCheckedAtUtc = DateTimeOffset.UtcNow,
            Error = error,
        });

    public Task<ServiceHealthSnapshot> ProbeAsync(string serviceName, Uri baseUrl, CancellationToken cancellationToken)
    {
        lock (_probedServices)
        {
            _probedServices.Add(serviceName);
        }

        if (_scripted.TryGetValue(serviceName, out var snap))
            return Task.FromResult(snap);

        return Task.FromResult(new ServiceHealthSnapshot
        {
            ServiceName = serviceName,
            Status = "unknown",
            Dependencies = Array.Empty<ServiceHealthSnapshot.DependencyEntry>(),
            LastCheckedAtUtc = DateTimeOffset.UtcNow,
            Error = "no scripted response",
        });
    }
}
