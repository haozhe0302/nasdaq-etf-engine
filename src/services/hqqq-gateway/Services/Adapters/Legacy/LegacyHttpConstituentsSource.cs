using Hqqq.Gateway.Configuration;
using Hqqq.Gateway.Services.Sources;

namespace Hqqq.Gateway.Services.Adapters.Legacy;

// TODO: Phase 2B — replace with RedisConstituentsSource reading from Redis
public sealed class LegacyHttpConstituentsSource(
    IHttpClientFactory httpClientFactory,
    ILogger<LegacyHttpConstituentsSource> logger) : IConstituentsSource
{
    public Task<IResult> GetConstituentsAsync(CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(GatewaySourceRegistration.LegacyHttpClientName);
        return LegacyForwarder.ForwardAsync(client, "/api/constituents", logger, ct);
    }
}
