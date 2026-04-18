using Hqqq.Gateway.Services.Sources;

namespace Hqqq.Gateway.Services.Adapters.Stub;

// TODO: Phase 2C3 — replace with gateway-native health aggregation over infra deps
public sealed class StubSystemHealthSource : ISystemHealthSource
{
    public Task<IResult> GetSystemHealthAsync(CancellationToken ct)
    {
        var payload = new
        {
            serviceName = "hqqq-gateway",
            status = "healthy",
            checkedAtUtc = DateTimeOffset.UtcNow,
            version = "0.0.0-stub",
            sourceMode = "stub",
            runtime = new
            {
                uptimeSeconds = 0L,
                memoryMb = 0L,
                gcGen0 = 0,
                gcGen1 = 0,
                gcGen2 = 0,
                threadCount = 0,
            },
            metrics = (object?)null,
            upstream = (object?)null,
            dependencies = Array.Empty<object>(),
        };

        return Task.FromResult(Results.Ok(payload));
    }
}
