using Hqqq.Gateway.Configuration;
using Hqqq.Gateway.Services.Sources;

namespace Hqqq.Gateway.Services.Adapters.Legacy;

// TODO: Phase 2B5 — replace with TimescaleHistorySource reading from TimescaleDB
public sealed class LegacyHttpHistorySource(
    IHttpClientFactory httpClientFactory,
    ILogger<LegacyHttpHistorySource> logger) : IHistorySource
{
    public Task<IResult> GetHistoryAsync(string? range, CancellationToken ct)
    {
        var path = string.IsNullOrWhiteSpace(range)
            ? "/api/history"
            : $"/api/history?range={Uri.EscapeDataString(range)}";

        var client = httpClientFactory.CreateClient(GatewaySourceRegistration.LegacyHttpClientName);
        return LegacyForwarder.ForwardAsync(client, path, logger, ct);
    }
}
