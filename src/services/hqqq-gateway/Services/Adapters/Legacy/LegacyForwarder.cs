using System.Net;

namespace Hqqq.Gateway.Services.Adapters.Legacy;

/// <summary>
/// Shared helper for passthrough forwarding to legacy hqqq-api.
/// On success, streams the upstream response body, content-type, and status code unchanged.
/// On failure, returns a controlled 502 JSON payload.
/// </summary>
public static class LegacyForwarder
{
    public static async Task<IResult> ForwardAsync(
        HttpClient client,
        string pathAndQuery,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var response = await client.GetAsync(pathAndQuery, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";

            return Results.Content(body, contentType, statusCode: (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            logger.LogWarning(ex, "Legacy upstream request to {Path} failed", pathAndQuery);
            return UpstreamErrorResult(pathAndQuery, ex);
        }
    }

    public static IResult UpstreamErrorResult(string path, Exception ex) =>
        Results.Json(new
        {
            status = "upstream_unavailable",
            message = $"Legacy upstream request failed: {ex.Message}",
            path,
        }, statusCode: (int)HttpStatusCode.BadGateway);
}
