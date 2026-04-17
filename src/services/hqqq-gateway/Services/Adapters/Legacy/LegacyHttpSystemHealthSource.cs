using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hqqq.Gateway.Configuration;
using Hqqq.Gateway.Services.Sources;

namespace Hqqq.Gateway.Services.Adapters.Legacy;

/// <summary>
/// Typed legacy adapter for /api/system/health.
/// Reads upstream JSON, preserves all fields, and additively overlays
/// gateway metadata (serviceName, sourceMode, upstream info).
/// </summary>
// TODO: Phase 2C3 — replace with gateway-native health aggregation over infra deps
public sealed class LegacyHttpSystemHealthSource(
    IHttpClientFactory httpClientFactory,
    ILogger<LegacyHttpSystemHealthSource> logger) : ISystemHealthSource
{
    public async Task<IResult> GetSystemHealthAsync(CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(GatewaySourceRegistration.LegacyHttpClientName);

        try
        {
            var response = await client.GetAsync("/api/system/health", ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return Results.Content(body,
                    response.Content.Headers.ContentType?.ToString() ?? "application/json",
                    statusCode: (int)response.StatusCode);
            }

            var node = JsonNode.Parse(body)?.AsObject();
            if (node is null)
                return Results.Content(body, "application/json", statusCode: (int)response.StatusCode);

            var upstreamServiceName = node["serviceName"]?.GetValue<string>();
            var upstreamCheckedAt = node["checkedAtUtc"]?.GetValue<string>();

            node["serviceName"] = "hqqq-gateway";
            node["sourceMode"] = "legacy";
            node["upstream"] = node["upstream"]?.DeepClone() ?? new JsonObject();
            if (node["upstream"] is JsonObject upstreamObj)
            {
                upstreamObj["serviceName"] = upstreamServiceName;
                upstreamObj["checkedAtUtc"] = upstreamCheckedAt;
            }

            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            return Results.Content(node.ToJsonString(options), "application/json", statusCode: 200);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            logger.LogWarning(ex, "Legacy upstream request to /api/system/health failed");
            return LegacyForwarder.UpstreamErrorResult("/api/system/health", ex);
        }
    }
}
