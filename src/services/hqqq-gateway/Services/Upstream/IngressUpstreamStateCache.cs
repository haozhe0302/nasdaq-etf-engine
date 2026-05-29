using Hqqq.Gateway.Services.Adapters.Aggregated;
using Microsoft.Extensions.Options;

namespace Hqqq.Gateway.Services.Upstream;

/// <summary>
/// Cached, in-process view of the ingress upstream transport state
/// (Tiingo WebSocket connected / REST fallback active). Refreshed on a
/// short background cadence by probing ingress <c>/healthz/ready</c> and
/// reading the <c>tiingo-upstream</c> dependency's structured data dict —
/// the same source the System page aggregation uses.
/// </summary>
/// <remarks>
/// The quote-serving hot path (REST snapshot + SignalR deltas) reads this
/// cache in-memory rather than probing ingress per request, which would be
/// untenable at delta cadence. When ingress is unreachable or not
/// configured the cache reports "no fresh state" and the enricher falls
/// back to whatever the quote-engine published.
/// </remarks>
public interface IIngressUpstreamState
{
    /// <summary>
    /// Returns the most recently observed upstream transport flags when a
    /// reading is available and still within the freshness window;
    /// otherwise returns <c>false</c> and the caller should not override.
    /// </summary>
    bool TryGet(out bool webSocketConnected, out bool fallbackActive);
}

public sealed class IngressUpstreamStateCache : BackgroundService, IIngressUpstreamState
{
    private const string IngressServiceKey = "Ingress";
    private const string IngressServiceName = "hqqq-ingress";
    private const string UpstreamDependencyName = "tiingo-upstream";

    /// <summary>How often we re-probe ingress for transport state.</summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(3);

    /// <summary>
    /// A reading older than this is treated as unavailable so a wedged or
    /// unreachable ingress degrades to "passthrough" instead of pinning a
    /// stale connected/disconnected flag onto every quote.
    /// </summary>
    private static readonly TimeSpan FreshnessWindow = TimeSpan.FromSeconds(15);

    private readonly IServiceHealthClient _client;
    private readonly GatewayHealthOptions _options;
    private readonly ILogger<IngressUpstreamStateCache> _logger;

    private volatile Reading? _last;

    private sealed record Reading(bool WebSocketConnected, bool FallbackActive, DateTimeOffset AtUtc);

    public IngressUpstreamStateCache(
        IServiceHealthClient client,
        IOptions<GatewayHealthOptions> options,
        ILogger<IngressUpstreamStateCache> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public bool TryGet(out bool webSocketConnected, out bool fallbackActive)
    {
        webSocketConnected = false;
        fallbackActive = false;

        var reading = _last;
        if (reading is null) return false;
        if (DateTimeOffset.UtcNow - reading.AtUtc > FreshnessWindow) return false;

        webSocketConnected = reading.WebSocketConnected;
        fallbackActive = reading.FallbackActive;
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var baseUri = ResolveIngressBaseUri();
        if (baseUri is null)
        {
            _logger.LogInformation(
                "Ingress base URL not configured; upstream feed enrichment is disabled (Market Data tile will reflect engine defaults).");
            return;
        }

        // Probe immediately so the first quotes after startup can be
        // enriched without waiting a full interval.
        await RefreshAsync(baseUri, stoppingToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(RefreshInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await RefreshAsync(baseUri, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown — expected.
        }
    }

    private async Task RefreshAsync(Uri baseUri, CancellationToken ct)
    {
        ServiceHealthSnapshot snapshot;
        try
        {
            snapshot = await _client.ProbeAsync(IngressServiceName, baseUri, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            // IServiceHealthClient is contractually non-throwing, but guard
            // anyway so a probe hiccup can never crash the background loop.
            _logger.LogDebug(ex, "Ingress upstream probe failed");
            return;
        }

        if (snapshot.Error is not null)
        {
            // Keep the last reading; it will age out of the freshness
            // window and the enricher will stop overriding.
            return;
        }

        foreach (var dep in snapshot.Dependencies)
        {
            if (!string.Equals(dep.Name, UpstreamDependencyName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (dep.Data is null) continue;

            var ws = ReadBool(dep.Data, "webSocketConnected", "isUpstreamConnected") ?? false;
            var fallback = ReadBool(dep.Data, "fallbackActive") ?? false;
            _last = new Reading(ws, fallback, DateTimeOffset.UtcNow);
            return;
        }
    }

    private Uri? ResolveIngressBaseUri()
    {
        _options.Services.TryGetValue(IngressServiceKey, out var endpoint);
        var raw = endpoint?.BaseUrl?.Trim();
        if (string.IsNullOrWhiteSpace(raw)) return null;

        if (Uri.TryCreate(raw, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return uri;
        }

        return null;
    }

    private static bool? ReadBool(IReadOnlyDictionary<string, object?> data, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (data.TryGetValue(key, out var value) && value is bool b) return b;
        }
        return null;
    }
}
