using System.Diagnostics.Metrics;

namespace Hqqq.Observability.Metrics;

/// <summary>
/// Shared meter and instrument instances that services can use directly.
/// Call <see cref="Hosting.ObservabilityRegistration.AddHqqqObservability"/> to register.
/// </summary>
public sealed class HqqqMetrics
{
    public static readonly Meter Meter = new(MetricNames.MeterName, "1.0.0");

    public Counter<long> TicksReceived { get; } =
        Meter.CreateCounter<long>(MetricNames.TicksReceived, "ticks");

    public Counter<long> TicksPublished { get; } =
        Meter.CreateCounter<long>(MetricNames.TicksPublished, "ticks");

    public Histogram<double> QuoteComputeDuration { get; } =
        Meter.CreateHistogram<double>(MetricNames.QuoteComputeDuration, "ms");

    public Counter<long> QuoteSnapshotsPublished { get; } =
        Meter.CreateCounter<long>(MetricNames.QuoteSnapshotsPublished, "snapshots");

    public Counter<long> BasketRefreshes { get; } =
        Meter.CreateCounter<long>(MetricNames.BasketRefreshes, "refreshes");

    public Counter<long> PersistenceRowsWritten { get; } =
        Meter.CreateCounter<long>(MetricNames.PersistenceRowsWritten, "rows");

    public UpDownCounter<long> GatewayActiveConnections { get; } =
        Meter.CreateUpDownCounter<long>(MetricNames.GatewayActiveConnections, "connections");

    public Histogram<double> GatewayRequestDuration { get; } =
        Meter.CreateHistogram<double>(MetricNames.GatewayRequestDuration, "ms");

    // Phase 2D2 — Redis pub/sub + SignalR live fan-out
    public Counter<long> QuoteUpdatesPublished { get; } =
        Meter.CreateCounter<long>(MetricNames.QuoteUpdatesPublished, "updates");

    public Counter<long> QuoteUpdatePublishFailures { get; } =
        Meter.CreateCounter<long>(MetricNames.QuoteUpdatePublishFailures, "failures");

    public Counter<long> GatewayQuoteUpdatesReceived { get; } =
        Meter.CreateCounter<long>(MetricNames.GatewayQuoteUpdatesReceived, "updates");

    public Counter<long> GatewayQuoteUpdatesMalformed { get; } =
        Meter.CreateCounter<long>(MetricNames.GatewayQuoteUpdatesMalformed, "updates");

    public Counter<long> GatewaySignalrBroadcasts { get; } =
        Meter.CreateCounter<long>(MetricNames.GatewaySignalrBroadcasts, "broadcasts");

    public Counter<long> GatewaySignalrBroadcastFailures { get; } =
        Meter.CreateCounter<long>(MetricNames.GatewaySignalrBroadcastFailures, "failures");

    // hqqq-reference-data — Kafka publish-health instrumentation. Gauges
    // are observable and pull their value from an
    // <c>IObservableGaugeSource</c> wired up in the service that owns the
    // state (see Hqqq.ReferenceData.Services.PublishHealthMetrics). The
    // counter below is incremented inline from the refresh pipeline on
    // every failed publish attempt.
    public Counter<long> RefdataPublishFailuresTotal { get; } =
        Meter.CreateCounter<long>(MetricNames.RefdataPublishFailuresTotal, "failures");

    // hqqq-reference-data — corporate-action + transition counters
    // incremented inline by the refresh pipeline.
    public Counter<long> RefdataSplitsAppliedTotal { get; } =
        Meter.CreateCounter<long>(MetricNames.RefdataSplitsAppliedTotal, "splits");

    public Counter<long> RefdataRenamesAppliedTotal { get; } =
        Meter.CreateCounter<long>(MetricNames.RefdataRenamesAppliedTotal, "renames");

    public Counter<long> RefdataBasketTransitionsTotal { get; } =
        Meter.CreateCounter<long>(MetricNames.RefdataBasketTransitionsTotal, "transitions");

    public Counter<long> RefdataCorpActionFetchErrorsTotal { get; } =
        Meter.CreateCounter<long>(MetricNames.RefdataCorpActionFetchErrorsTotal, "errors");
}
