namespace Hqqq.Persistence.Options;

/// <summary>
/// Persistence-only knobs bound from the "Persistence" configuration section.
/// Kafka topic names, connection strings, and JSON defaults live in shared
/// infrastructure (<see cref="Hqqq.Infrastructure.Kafka.KafkaTopics"/>,
/// <see cref="Hqqq.Infrastructure.Kafka.KafkaOptions"/>,
/// <see cref="Hqqq.Infrastructure.Timescale.TimescaleOptions"/>,
/// <see cref="Hqqq.Infrastructure.Serialization.HqqqJsonDefaults"/>) and are
/// intentionally not duplicated here.
/// </summary>
public sealed class PersistenceOptions
{
    /// <summary>
    /// When true (default), the service ensures the Timescale schema on
    /// startup via <see cref="Schema.QuoteSnapshotSchemaBootstrapper"/>.
    /// Bootstrap failures fail the host fast; malformed Kafka events never do.
    /// Disable in tests or environments where a migration/owner process has
    /// already provisioned the tables.
    /// </summary>
    public bool SchemaBootstrapOnStart { get; set; } = true;

    /// <summary>
    /// Maximum number of rows combined into a single Timescale INSERT. A
    /// flush also triggers when <see cref="SnapshotFlushInterval"/> elapses.
    /// </summary>
    public int SnapshotWriteBatchSize { get; set; } = 128;

    /// <summary>
    /// Upper bound on the delay between receiving a snapshot and writing it,
    /// even if the batch is only partially full. Keeps the write tail short
    /// at low ingest rates.
    /// </summary>
    public TimeSpan SnapshotFlushInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Capacity of the in-proc channel between the consumer and the worker.
    /// Bounded on purpose so Kafka consumption applies backpressure when the
    /// writer cannot keep up.
    /// </summary>
    public int SnapshotChannelCapacity { get; set; } = 2048;
}
