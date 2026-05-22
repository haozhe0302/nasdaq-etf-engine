using Hqqq.Persistence.Options;
using Hqqq.Persistence.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Hqqq.Persistence.Tests.Workers;

/// <summary>
/// Exercises the bootstrap orchestration directly, without a live Timescale,
/// to lock in the contract that:
/// <list type="bullet">
///   <item><description>base hypertable bootstrap is always run;</description></item>
///   <item><description>continuous-aggregate rollups are gated by
///     <see cref="PersistenceOptions.EnableContinuousAggregates"/>;</description></item>
///   <item><description>a Timescale "feature not supported" error (SQLSTATE
///     <c>0A000</c>) is downgraded to a warning under
///     <see cref="PersistenceOptions.ContinueOnUnsupportedRollups"/> so the
///     host does not crash on Apache-only TimescaleDB builds;</description></item>
///   <item><description>retention policies skip rollup views whenever the
///     rollup step did not actually create them.</description></item>
/// </list>
/// </summary>
public class SchemaBootstrapPipelineTests
{
    private sealed class CallLog
    {
        public int SnapshotSchema;
        public int RawTickSchema;
        public int Rollups;
        public int Retention;
        public bool? RetentionIncludedRollups;
    }

    private static PostgresException FeatureNotSupported(string message) =>
        new(
            messageText: message,
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: SchemaBootstrapPipeline.FeatureNotSupportedSqlState);

    [Fact]
    public async Task SchemaBootstrapOnStart_False_SkipsEverything()
    {
        var log = new CallLog();
        var options = new PersistenceOptions { SchemaBootstrapOnStart = false };

        await RunAsync(options, log);

        Assert.Equal(0, log.SnapshotSchema);
        Assert.Equal(0, log.RawTickSchema);
        Assert.Equal(0, log.Rollups);
        Assert.Equal(0, log.Retention);
    }

    [Fact]
    public async Task DefaultOptions_RunEveryStepInOrder_AndIncludeRollupRetention()
    {
        var log = new CallLog();
        var options = new PersistenceOptions();

        await RunAsync(options, log);

        Assert.Equal(1, log.SnapshotSchema);
        Assert.Equal(1, log.RawTickSchema);
        Assert.Equal(1, log.Rollups);
        Assert.Equal(1, log.Retention);
        Assert.True(log.RetentionIncludedRollups);
    }

    [Fact]
    public async Task EnableContinuousAggregates_False_SkipsRollups_AndDropsRollupRetention()
    {
        var log = new CallLog();
        var options = new PersistenceOptions { EnableContinuousAggregates = false };

        await RunAsync(options, log);

        Assert.Equal(1, log.SnapshotSchema);
        Assert.Equal(1, log.RawTickSchema);
        Assert.Equal(0, log.Rollups);
        Assert.Equal(1, log.Retention);
        Assert.False(log.RetentionIncludedRollups);
    }

    [Fact]
    public async Task Rollups_RaiseFeatureNotSupported_LogsWarning_AndContinues()
    {
        var log = new CallLog();
        var options = new PersistenceOptions
        {
            EnableContinuousAggregates = true,
            ContinueOnUnsupportedRollups = true,
        };

        await RunAsync(
            options,
            log,
            rollups: _ => throw FeatureNotSupported(
                "functionality not supported under the current \"apache\" license"));

        Assert.Equal(1, log.SnapshotSchema);
        Assert.Equal(1, log.RawTickSchema);
        Assert.Equal(1, log.Rollups); // attempted once, failure swallowed
        Assert.Equal(1, log.Retention);
        Assert.False(log.RetentionIncludedRollups);
    }

    [Fact]
    public async Task Rollups_RaiseFeatureNotSupported_WithContinueDisabled_PropagatesAndFailsFast()
    {
        var log = new CallLog();
        var options = new PersistenceOptions
        {
            EnableContinuousAggregates = true,
            ContinueOnUnsupportedRollups = false,
        };

        await Assert.ThrowsAsync<PostgresException>(() => RunAsync(
            options,
            log,
            rollups: _ => throw FeatureNotSupported("feature not supported")));

        Assert.Equal(1, log.SnapshotSchema);
        Assert.Equal(1, log.RawTickSchema);
        Assert.Equal(1, log.Rollups); // attempted, then rethrew
        Assert.Equal(0, log.Retention);
    }

    [Fact]
    public async Task Rollups_RaiseUnrelatedPostgresError_StillFailsFast()
    {
        var log = new CallLog();
        var options = new PersistenceOptions
        {
            EnableContinuousAggregates = true,
            ContinueOnUnsupportedRollups = true,
        };

        await Assert.ThrowsAsync<PostgresException>(() => RunAsync(
            options,
            log,
            rollups: _ => throw new PostgresException(
                messageText: "syntax error",
                severity: "ERROR",
                invariantSeverity: "ERROR",
                sqlState: "42601")));

        Assert.Equal(1, log.SnapshotSchema);
        Assert.Equal(1, log.RawTickSchema);
        Assert.Equal(1, log.Rollups);
        Assert.Equal(0, log.Retention);
    }

    [Fact]
    public async Task Retention_RaisesFeatureNotSupported_LogsWarning_AndContinues()
    {
        var log = new CallLog();
        var options = new PersistenceOptions
        {
            ContinueOnUnsupportedRollups = true,
        };

        await RunAsync(
            options,
            log,
            retention: (_, _) => throw FeatureNotSupported(
                "add_retention_policy not supported"));

        Assert.Equal(1, log.SnapshotSchema);
        Assert.Equal(1, log.RawTickSchema);
        Assert.Equal(1, log.Rollups);
        Assert.Equal(1, log.Retention); // attempted, failure swallowed
    }

    [Fact]
    public async Task EnableRetentionPolicies_False_SkipsRetention()
    {
        var log = new CallLog();
        var options = new PersistenceOptions { EnableRetentionPolicies = false };

        await RunAsync(options, log);

        Assert.Equal(1, log.SnapshotSchema);
        Assert.Equal(1, log.RawTickSchema);
        Assert.Equal(1, log.Rollups);
        Assert.Equal(0, log.Retention);
    }

    [Fact]
    public async Task BaseHypertableFailure_IsAlwaysFatal_EvenOnFeatureNotSupported()
    {
        var log = new CallLog();
        var options = new PersistenceOptions
        {
            ContinueOnUnsupportedRollups = true,
        };

        await Assert.ThrowsAsync<PostgresException>(() => RunAsync(
            options,
            log,
            snapshotSchema: _ => throw FeatureNotSupported(
                "hypertable creation blocked")));

        Assert.Equal(0, log.RawTickSchema);
        Assert.Equal(0, log.Rollups);
        Assert.Equal(0, log.Retention);
    }

    private static Task RunAsync(
        PersistenceOptions options,
        CallLog log,
        Func<CancellationToken, Task>? snapshotSchema = null,
        Func<CancellationToken, Task>? rawTickSchema = null,
        Func<CancellationToken, Task>? rollups = null,
        Func<bool, CancellationToken, Task>? retention = null)
    {
        return SchemaBootstrapPipeline.RunAsync(
            options,
            ct =>
            {
                log.SnapshotSchema++;
                return snapshotSchema?.Invoke(ct) ?? Task.CompletedTask;
            },
            ct =>
            {
                log.RawTickSchema++;
                return rawTickSchema?.Invoke(ct) ?? Task.CompletedTask;
            },
            ct =>
            {
                log.Rollups++;
                return rollups?.Invoke(ct) ?? Task.CompletedTask;
            },
            (includeRollups, ct) =>
            {
                log.Retention++;
                log.RetentionIncludedRollups = includeRollups;
                return retention?.Invoke(includeRollups, ct) ?? Task.CompletedTask;
            },
            NullLogger.Instance,
            CancellationToken.None);
    }
}
