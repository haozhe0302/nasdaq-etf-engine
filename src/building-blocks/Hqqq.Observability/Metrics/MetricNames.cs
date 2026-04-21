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

    // Phase 2D2 — Redis pub/sub + SignalR live fan-out
    public const string QuoteUpdatesPublished = "hqqq.quote_engine.quote_updates_published";
    public const string QuoteUpdatePublishFailures = "hqqq.quote_engine.quote_update_publish_failures";
    public const string GatewayQuoteUpdatesReceived = "hqqq.gateway.quote_updates_received";
    public const string GatewayQuoteUpdatesMalformed = "hqqq.gateway.quote_updates_malformed";
    public const string GatewaySignalrBroadcasts = "hqqq.gateway.signalr_broadcasts";
    public const string GatewaySignalrBroadcastFailures = "hqqq.gateway.signalr_broadcast_failures";

    // hqqq-reference-data — active-basket publish health (readiness-grade)
    public const string RefdataLastPublishOkTimestamp = "hqqq.refdata.last_publish_ok_timestamp";
    public const string RefdataConsecutivePublishFailures = "hqqq.refdata.consecutive_publish_failures";
    public const string RefdataPublishFailuresTotal = "hqqq.refdata.publish_failures_total";
    public const string RefdataPublishOutageSeconds = "hqqq.refdata.publish_outage_seconds";

    // hqqq-reference-data — corporate-action + transition counters.
    public const string RefdataSplitsAppliedTotal = "hqqq.refdata.splits_applied_total";
    public const string RefdataRenamesAppliedTotal = "hqqq.refdata.renames_applied_total";
    public const string RefdataBasketTransitionsTotal = "hqqq.refdata.basket_transitions_total";
    public const string RefdataCorpActionFetchErrorsTotal = "hqqq.refdata.corp_action_fetch_errors_total";

    // hqqq-ingress — observability for basket-driven subscription and
    // the runtime tick-flow signal that smoke proofs sample.
    public const string IngressActiveSymbols = "hqqq.ingress.active_symbols";
    public const string IngressBasketFingerprintAgeSeconds = "hqqq.ingress.basket_fingerprint_age_seconds";
    public const string IngressPublishedTicksTotal = "hqqq.ingress.published_ticks_total";
    public const string IngressLastPublishedTickTimestamp = "hqqq.ingress.last_published_tick_timestamp";
}
