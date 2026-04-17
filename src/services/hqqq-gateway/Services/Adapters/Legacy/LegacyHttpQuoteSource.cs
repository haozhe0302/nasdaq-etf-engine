using Hqqq.Gateway.Configuration;
using Hqqq.Gateway.Services.Sources;

namespace Hqqq.Gateway.Services.Adapters.Legacy;

// TODO: Phase 2B — replace with RedisQuoteSource reading latest snapshot from Redis
public sealed class LegacyHttpQuoteSource(
    IHttpClientFactory httpClientFactory,
    ILogger<LegacyHttpQuoteSource> logger) : IQuoteSource
{
    public Task<IResult> GetQuoteAsync(CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(GatewaySourceRegistration.LegacyHttpClientName);
        return LegacyForwarder.ForwardAsync(client, "/api/quote", logger, ct);
    }
}
