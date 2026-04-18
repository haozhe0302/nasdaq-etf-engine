using System.Diagnostics.Metrics;

namespace Hqqq.Observability.Metrics;

/// <summary>
/// Shared meter and instrument instances that services can use directly.
/// Call <see cref="Logging.LoggingExtensions.AddHqqqObservability"/> to register.
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
}
