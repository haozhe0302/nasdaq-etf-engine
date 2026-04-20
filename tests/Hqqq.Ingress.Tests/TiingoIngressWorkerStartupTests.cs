using Hqqq.Infrastructure.Hosting;
using Hqqq.Ingress.Clients;
using Hqqq.Ingress.Configuration;
using Hqqq.Ingress.Publishing;
using Hqqq.Ingress.State;
using Hqqq.Ingress.Workers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hqqq.Ingress.Tests;

/// <summary>
/// StartAsync gates the worker on operating mode + Tiingo API key. These
/// tests assert the operator-visible behaviour without needing a real
/// websocket / kafka.
/// </summary>
public class TiingoIngressWorkerStartupTests
{
    [Fact]
    public async Task Standalone_WithMissingApiKey_FailsFast()
    {
        var worker = BuildWorker(
            mode: OperatingMode.Standalone,
            apiKey: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => worker.StartAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("<set tiingo api key>")]
    [InlineData("YOUR_API_KEY")]
    [InlineData("changeme")]
    [InlineData("   ")]
    public async Task Standalone_WithPlaceholderApiKey_FailsFast(string placeholder)
    {
        var worker = BuildWorker(
            mode: OperatingMode.Standalone,
            apiKey: placeholder);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => worker.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Hybrid_WithMissingApiKey_StartsCleanly()
    {
        var worker = BuildWorker(mode: OperatingMode.Hybrid, apiKey: null);

        // Hybrid mode is a no-op stub; StartAsync must complete without
        // throwing even when no Tiingo credentials are configured.
        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Hybrid_WithApiKey_LogsWarningAndStillStarts()
    {
        var captured = new CapturingLoggerProvider();
        var worker = BuildWorker(
            mode: OperatingMode.Hybrid,
            apiKey: "real-looking-key",
            loggerProvider: captured);

        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        Assert.Contains(captured.Entries, e =>
            e.Level == LogLevel.Warning
            && e.Message.Contains("hybrid mode", StringComparison.OrdinalIgnoreCase)
            && e.Message.Contains("Tiingo:ApiKey", StringComparison.OrdinalIgnoreCase));
    }

    private static TiingoIngressWorker BuildWorker(
        OperatingMode mode,
        string? apiKey,
        ILoggerProvider? loggerProvider = null)
    {
        var loggerFactory = loggerProvider is null
            ? (ILoggerFactory)NullLoggerFactory.Instance
            : LoggerFactory.Create(b => b.AddProvider(loggerProvider));

        return new TiingoIngressWorker(
            streamClient: new StubTiingoStreamClient(loggerFactory.CreateLogger<StubTiingoStreamClient>()),
            snapshotClient: new StubTiingoSnapshotClient(loggerFactory.CreateLogger<StubTiingoSnapshotClient>()),
            publisher: new LoggingTickPublisher(loggerFactory.CreateLogger<LoggingTickPublisher>()),
            state: new IngestionState(),
            options: Options.Create(new TiingoOptions { ApiKey = apiKey, SnapshotOnStartup = false }),
            mode: new OperatingModeOptions { Mode = mode },
            logger: loggerFactory.CreateLogger<TiingoIngressWorker>());
    }
}
