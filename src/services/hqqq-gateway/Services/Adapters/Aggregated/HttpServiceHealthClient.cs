using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace Hqqq.Gateway.Services.Adapters.Aggregated;

/// <summary>
/// Default <see cref="IServiceHealthClient"/>: GETs <c>/healthz/ready</c> on
/// each downstream service through a named <see cref="IHttpClientFactory"/>
/// client (<c>health-aggregator</c>) and parses the standard
/// <c>HealthzPayloadBuilder</c>-shaped response. Bounded by
/// <see cref="GatewayHealthOptions.RequestTimeoutSeconds"/> per call and
/// strictly non-throwing — every failure becomes
/// <see cref="ServiceHealthSnapshot.Error"/>.
/// </summary>
public sealed class HttpServiceHealthClient : IServiceHealthClient
{
    public const string HttpClientName = "health-aggregator";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GatewayHealthOptions _options;
    private readonly ILogger<HttpServiceHealthClient> _logger;

    public HttpServiceHealthClient(
        IHttpClientFactory httpClientFactory,
        IOptions<GatewayHealthOptions> options,
        ILogger<HttpServiceHealthClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ServiceHealthSnapshot> ProbeAsync(
        string serviceName,
        Uri baseUrl,
        CancellationToken cancellationToken)
    {
        var checkedAt = DateTimeOffset.UtcNow;
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            var url = new Uri(baseUrl, "/healthz/ready");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(Math.Max(0.1, _options.RequestTimeoutSeconds)));

            using var response = await client.GetAsync(url, cts.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

            // Downstream services emit 503 from /healthz/ready when degraded
            // — the body is still the standard payload, so parse on any
            // 2xx/5xx with content; only treat parse failure as Error.
            JsonNode? node = null;
            try
            {
                node = JsonNode.Parse(body);
            }
            catch (JsonException) { /* fall through, treated as malformed */ }

            if (node is not JsonObject obj)
            {
                return Error(serviceName, checkedAt,
                    $"non-json response (HTTP {(int)response.StatusCode})");
            }

            var deps = new List<ServiceHealthSnapshot.DependencyEntry>();
            if (obj["dependencies"] is JsonArray depsArr)
            {
                foreach (var d in depsArr)
                {
                    if (d is not JsonObject dobj) continue;
                    var name = dobj["name"]?.GetValue<string>();
                    var status = dobj["status"]?.GetValue<string>();
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(status))
                        deps.Add(new ServiceHealthSnapshot.DependencyEntry(name, status));
                }
            }

            return new ServiceHealthSnapshot
            {
                ServiceName = obj["serviceName"]?.GetValue<string>() ?? serviceName,
                Status = obj["status"]?.GetValue<string>() ?? "unknown",
                Version = obj["serviceVersion"]?.GetValue<string>(),
                UptimeSeconds = obj["uptimeSeconds"]?.GetValue<long?>(),
                Dependencies = deps,
                LastCheckedAtUtc = checkedAt,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller cancelled — propagate quietly via Error so the aggregator
            // still produces a payload.
            return Error(serviceName, checkedAt, "cancelled");
        }
        catch (OperationCanceledException)
        {
            return Error(serviceName, checkedAt, "timeout");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "Health probe to {Service} failed", serviceName);
            return Error(serviceName, checkedAt, $"unreachable: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error probing {Service}", serviceName);
            return Error(serviceName, checkedAt, $"error: {ex.GetType().Name}");
        }
    }

    private static ServiceHealthSnapshot Error(string serviceName, DateTimeOffset checkedAt, string error)
        => new()
        {
            ServiceName = serviceName,
            Status = "unknown",
            Dependencies = Array.Empty<ServiceHealthSnapshot.DependencyEntry>(),
            LastCheckedAtUtc = checkedAt,
            Error = error,
        };
}
