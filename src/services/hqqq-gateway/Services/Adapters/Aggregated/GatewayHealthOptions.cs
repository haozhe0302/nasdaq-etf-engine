namespace Hqqq.Gateway.Services.Adapters.Aggregated;

/// <summary>
/// Bound from the <c>Gateway:Health</c> configuration section. Drives the
/// native <c>/api/system/health</c> aggregator: per-service base URLs and
/// the per-call timeout for downstream probes.
/// </summary>
public sealed class GatewayHealthOptions
{
    public const string SectionName = "Gateway:Health";

    /// <summary>
    /// Per-service health probe timeout, applied to each downstream call
    /// independently. Kept short so a single hung worker can't slow the
    /// aggregated payload past frontend tolerance.
    /// </summary>
    public double RequestTimeoutSeconds { get; set; } = 1.5;

    /// <summary>
    /// Map of well-known service keys (<c>ReferenceData</c>, <c>Ingress</c>,
    /// <c>QuoteEngine</c>, <c>Persistence</c>, <c>Analytics</c>) to a
    /// <see cref="ServiceEndpointOptions"/> describing where each one's
    /// management endpoint lives. Services with a missing or empty
    /// <c>BaseUrl</c> surface in the aggregated payload as <c>idle</c>
    /// (not configured) instead of <c>unknown</c>.
    /// </summary>
    public Dictionary<string, ServiceEndpointOptions> Services { get; set; } = new();

    /// <summary>
    /// Whether to include the local Redis health check in the aggregated
    /// payload. True by default; flip to false when running in stub mode
    /// where no Redis is provisioned.
    /// </summary>
    public bool IncludeRedis { get; set; } = true;

    /// <summary>
    /// Whether to include the local Timescale health check in the
    /// aggregated payload. True by default.
    /// </summary>
    public bool IncludeTimescale { get; set; } = true;

    public sealed class ServiceEndpointOptions
    {
        public string? BaseUrl { get; set; }
    }

    /// <summary>
    /// Stable, ordered list of downstream service keys probed by the
    /// aggregator. The order controls the order they appear in the
    /// rendered <c>dependencies[]</c> array.
    /// </summary>
    public static readonly IReadOnlyList<(string Key, string ServiceName)> KnownServices =
        new[]
        {
            ("ReferenceData", "hqqq-reference-data"),
            ("Ingress", "hqqq-ingress"),
            ("QuoteEngine", "hqqq-quote-engine"),
            ("Persistence", "hqqq-persistence"),
            ("Analytics", "hqqq-analytics"),
        };
}
