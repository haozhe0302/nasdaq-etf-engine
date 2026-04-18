using Hqqq.Analytics.Options;
using Hqqq.Analytics.Reports;
using Hqqq.Analytics.Services;
using Hqqq.Analytics.Tests.Fakes;
using Hqqq.Analytics.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace Hqqq.Analytics.Tests.Workers;

public class SnapshotQualityReportJobTests
{
    private static AnalyticsOptions ReportOptions(string? emitJsonPath = null, bool includeTicks = false)
        => new()
        {
            Mode = AnalyticsOptions.ReportMode,
            BasketId = "HQQQ",
            StartUtc = SnapshotFixture.T(14),
            EndUtc = SnapshotFixture.T(15),
            EmitJsonPath = emitJsonPath,
            IncludeRawTickAggregates = includeTicks,
        };

    private static SnapshotQualityReportJob BuildJob(
        FakeQuoteSnapshotReader reader,
        FakeRawTickAggregateReader? rawReader = null,
        AnalyticsOptions? options = null,
        JsonReportEmitter? emitter = null)
        => new(
            reader,
            rawReader ?? new FakeRawTickAggregateReader(),
            emitter ?? new JsonReportEmitter(),
            MsOptions.Create(options ?? ReportOptions()),
            NullLogger<SnapshotQualityReportJob>.Instance);

    [Fact]
    public async Task RunAsync_HappyPath_Completes()
    {
        var rows = new[]
        {
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 0)),
            SnapshotFixture.Row(SnapshotFixture.T(14, 0, 1)),
        };
        var reader = new FakeQuoteSnapshotReader(rows);
        var job = BuildJob(reader);

        await job.RunAsync(CancellationToken.None);

        Assert.Equal(1, reader.CallCount);
    }

    [Fact]
    public async Task RunAsync_EmptyWindow_DoesNotThrow()
    {
        var reader = new FakeQuoteSnapshotReader(Array.Empty<Hqqq.Analytics.Timescale.QuoteSnapshotRecord>());
        var job = BuildJob(reader);

        // Must not throw; the warning is logged and the host returns cleanly.
        await job.RunAsync(CancellationToken.None);

        Assert.Equal(1, reader.CallCount);
    }

    [Fact]
    public async Task RunAsync_IncludeRawTickAggregates_CallsAggregateReader()
    {
        var reader = new FakeQuoteSnapshotReader(new[] { SnapshotFixture.Row(SnapshotFixture.T(14, 0, 0)) });
        var rawReader = new FakeRawTickAggregateReader(count: 123);
        var job = BuildJob(reader, rawReader, ReportOptions(includeTicks: true));

        await job.RunAsync(CancellationToken.None);

        Assert.Equal(1, rawReader.CallCount);
    }

    [Fact]
    public async Task RunAsync_SkipsRawTickAggregate_WhenDisabled()
    {
        var reader = new FakeQuoteSnapshotReader(new[] { SnapshotFixture.Row(SnapshotFixture.T(14, 0, 0)) });
        var rawReader = new FakeRawTickAggregateReader();
        var job = BuildJob(reader, rawReader, ReportOptions(includeTicks: false));

        await job.RunAsync(CancellationToken.None);

        Assert.Equal(0, rawReader.CallCount);
    }

    [Fact]
    public async Task RunAsync_EmitJsonPath_WritesArtifact()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"hqqq-job-{Guid.NewGuid():N}.json");
        try
        {
            var reader = new FakeQuoteSnapshotReader(new[] { SnapshotFixture.Row(SnapshotFixture.T(14, 0, 0)) });
            var job = BuildJob(reader, options: ReportOptions(emitJsonPath: tmp));

            await job.RunAsync(CancellationToken.None);

            Assert.True(File.Exists(tmp));
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public async Task RunAsync_ReaderThrows_PropagatesException()
    {
        var reader = new FakeQuoteSnapshotReader(new InvalidOperationException("boom"));
        var job = BuildJob(reader);

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.RunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_MissingWindow_Throws()
    {
        var reader = new FakeQuoteSnapshotReader(Array.Empty<Hqqq.Analytics.Timescale.QuoteSnapshotRecord>());
        var job = BuildJob(reader, options: new AnalyticsOptions { Mode = AnalyticsOptions.ReportMode });

        await Assert.ThrowsAsync<InvalidOperationException>(() => job.RunAsync(CancellationToken.None));
    }
}

public class ReportJobDispatcherTests
{
    private sealed class RecordingJob : IReportJob
    {
        public RecordingJob(string mode, Func<CancellationToken, Task>? run = null)
        {
            Mode = mode;
            _run = run ?? (_ => Task.CompletedTask);
        }

        private readonly Func<CancellationToken, Task> _run;

        public string Mode { get; }
        public bool Ran { get; private set; }

        public async Task RunAsync(CancellationToken ct)
        {
            await _run(ct);
            Ran = true;
        }
    }

    private static AnalyticsOptions OptionsFor(string mode) => new() { Mode = mode };

    [Fact]
    public async Task Dispatch_KnownMode_RunsJobAndStopsApp()
    {
        var job = new RecordingJob("report");
        var lifetime = new FakeHostApplicationLifetime();
        var dispatcher = new ReportJobDispatcher(
            new[] { job }, lifetime, MsOptions.Create(OptionsFor("report")),
            NullLogger<ReportJobDispatcher>.Instance);

        await dispatcher.StartAsync(CancellationToken.None);
        await dispatcher.StopAsync(CancellationToken.None);

        Assert.True(job.Ran);
        Assert.True(lifetime.StopApplicationCalled);
    }

    [Fact]
    public async Task Dispatch_UnknownMode_SetsExitCodeAndStopsApp()
    {
        Environment.ExitCode = 0;
        var job = new RecordingJob("report");
        var lifetime = new FakeHostApplicationLifetime();
        var dispatcher = new ReportJobDispatcher(
            new[] { job }, lifetime, MsOptions.Create(OptionsFor("replay")),
            NullLogger<ReportJobDispatcher>.Instance);

        try
        {
            await dispatcher.StartAsync(CancellationToken.None);
            await dispatcher.StopAsync(CancellationToken.None);

            Assert.False(job.Ran);
            Assert.True(lifetime.StopApplicationCalled);
            Assert.Equal(2, Environment.ExitCode);
        }
        finally
        {
            Environment.ExitCode = 0;
        }
    }

    [Fact]
    public async Task Dispatch_JobThrows_SetsExitCodeAndStopsApp()
    {
        Environment.ExitCode = 0;
        var job = new RecordingJob("report", _ => throw new InvalidOperationException("boom"));
        var lifetime = new FakeHostApplicationLifetime();
        var dispatcher = new ReportJobDispatcher(
            new[] { job }, lifetime, MsOptions.Create(OptionsFor("report")),
            NullLogger<ReportJobDispatcher>.Instance);

        try
        {
            await dispatcher.StartAsync(CancellationToken.None);
            await dispatcher.StopAsync(CancellationToken.None);

            Assert.True(lifetime.StopApplicationCalled);
            Assert.Equal(1, Environment.ExitCode);
        }
        finally
        {
            Environment.ExitCode = 0;
        }
    }
}

