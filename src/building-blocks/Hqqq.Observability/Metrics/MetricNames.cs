namespace Hqqq.Observability.Metrics;

/// <summary>
/// Central registry of metric names used across all services.
/// Follows the OpenTelemetry semantic conventions for naming.
/// </summary>
public static class MetricNames
{
    public const string MeterName = "Hqqq";

    public const string TicksReceived = "hqqq.ingress.ticks_received";
    public const string TicksPublished = "hqqq.ingress.ticks_published";
    public const string QuoteComputeDuration = "hqqq.quote_engine.compute_duration_ms";
    public const string QuoteSnapshotsPublished = "hqqq.quote_engine.snapshots_published";
    public const string BasketRefreshes = "hqqq.refdata.basket_refreshes";
    public const string PersistenceRowsWritten = "hqqq.persistence.rows_written";
    public const string GatewayActiveConnections = "hqqq.gateway.active_connections";
    public const string GatewayRequestDuration = "hqqq.gateway.request_duration_ms";
    public const string HealthCheckDuration = "hqqq.health.check_duration_ms";
}
