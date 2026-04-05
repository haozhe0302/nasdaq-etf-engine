using System.Text.Json;
using Hqqq.Api.Configuration;
using Hqqq.Api.Modules.Benchmark.Contracts;
using Hqqq.Api.Modules.Benchmark.Services;
using Microsoft.Extensions.Options;

namespace Hqqq.Api.Tests.Benchmark;

public class BenchmarkTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ── Event roundtrip serialization ────────────────────

    [Fact]
    public void TickEvent_RoundtripsToJson()
    {
        var evt = new RecordedEvent
        {
            EventType = "tick",
            Timestamp = new DateTimeOffset(2026, 4, 4, 14, 30, 0, TimeSpan.Zero),
            Symbol = "AAPL",
            Price = 195.50m,
            Source = "ws",
            UpstreamTimestamp = new DateTimeOffset(2026, 4, 4, 14, 30, 0, TimeSpan.Zero),
        };

        var json = JsonSerializer.Serialize(evt, JsonOpts);
        var parsed = JsonSerializer.Deserialize<RecordedEvent>(json, JsonOpts)!;

        Assert.Equal("tick", parsed.EventType);
        Assert.Equal("AAPL", parsed.Symbol);
        Assert.Equal(195.50m, parsed.Price);
        Assert.Equal("ws", parsed.Source);
    }

    [Fact]
    public void QuoteEvent_RoundtripsToJson()
    {
        var evt = new RecordedEvent
        {
            EventType = "quote",
            Timestamp = new DateTimeOffset(2026, 4, 4, 14, 30, 1, TimeSpan.Zero),
            Nav = 487.12m,
            MarketPrice = 487.05m,
            PremiumDiscountBps = -1.5m,
            SymbolsTotal = 102,
            SymbolsStale = 3,
            BroadcastMs = 1.234,
            TickToQuoteMs = 45.678,
        };

        var json = JsonSerializer.Serialize(evt, JsonOpts);
        var parsed = JsonSerializer.Deserialize<RecordedEvent>(json, JsonOpts)!;

        Assert.Equal("quote", parsed.EventType);
        Assert.Equal(487.12m, parsed.Nav);
        Assert.Equal(102, parsed.SymbolsTotal);
        Assert.Equal(45.678, parsed.TickToQuoteMs);
    }

    [Fact]
    public void TransportEvent_RoundtripsToJson()
    {
        var evt = new RecordedEvent
        {
            EventType = "transport",
            Timestamp = new DateTimeOffset(2026, 4, 4, 14, 30, 5, TimeSpan.Zero),
            Action = "fallback_activated",
        };

        var json = JsonSerializer.Serialize(evt, JsonOpts);
        var parsed = JsonSerializer.Deserialize<RecordedEvent>(json, JsonOpts)!;

        Assert.Equal("transport", parsed.EventType);
        Assert.Equal("fallback_activated", parsed.Action);
    }

    // ── EventRecorderService ─────────────────────────────

    [Fact]
    public void Recorder_Disabled_DoesNotCreateFiles()
    {
        var opts = Options.Create(new RecordingOptions { Enabled = false });
        using var recorder = new EventRecorderService(opts);

        recorder.RecordTick("AAPL", 200m, "ws", null);
        recorder.RecordTransport("fallback_activated");

        Assert.Null(recorder.SessionPath);
    }

    [Fact]
    public void Recorder_Enabled_WritesJsonl()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hqqq-test-{Guid.NewGuid():N}");
        try
        {
            var opts = Options.Create(new RecordingOptions
            {
                Enabled = true,
                Directory = dir,
            });
            using var recorder = new EventRecorderService(opts);

            recorder.RecordTick("AAPL", 200m, "ws", DateTimeOffset.UtcNow);
            recorder.RecordQuote(487m, 487.05m, -1.0m, 100, 2, 1.5, 50.0);
            recorder.RecordTransport("fallback_activated");
            recorder.RecordActivation("fp123", 2.5);
            recorder.Dispose();

            Assert.NotNull(recorder.SessionPath);
            var lines = File.ReadAllLines(recorder.SessionPath);
            Assert.Equal(4, lines.Length);

            var tick = JsonSerializer.Deserialize<RecordedEvent>(lines[0], JsonOpts)!;
            Assert.Equal("tick", tick.EventType);
            Assert.Equal("AAPL", tick.Symbol);

            var quote = JsonSerializer.Deserialize<RecordedEvent>(lines[1], JsonOpts)!;
            Assert.Equal("quote", quote.EventType);
            Assert.Equal(487m, quote.Nav);

            var transport = JsonSerializer.Deserialize<RecordedEvent>(lines[2], JsonOpts)!;
            Assert.Equal("transport", transport.EventType);

            var activation = JsonSerializer.Deserialize<RecordedEvent>(lines[3], JsonOpts)!;
            Assert.Equal("activation", activation.EventType);
            Assert.Equal(2.5, activation.JumpBps);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void EndToEnd_RecorderOutput_ParsedByReplayEngine()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hqqq-e2e-{Guid.NewGuid():N}");
        try
        {
            var opts = Options.Create(new RecordingOptions
            {
                Enabled = true,
                Directory = dir,
            });
            using var recorder = new EventRecorderService(opts);

            recorder.RecordTick("AAPL", 200m, "ws", DateTimeOffset.UtcNow);
            recorder.RecordTick("MSFT", 400m, "rest", DateTimeOffset.UtcNow);
            recorder.RecordTransport("fallback_activated");
            recorder.RecordQuote(487m, 487.05m, -1.0m, 100, 3, 1.5, 52.3);
            recorder.RecordQuote(487.10m, 487.05m, 1.0m, 100, 0, 2.1, 61.0);
            recorder.RecordTransport("ws_recovered");
            recorder.RecordActivation("fp-e2e", 3.7);
            recorder.Dispose();

            var report = Hqqq.Bench.ReplayEngine.Run(recorder.SessionPath!);

            Assert.Equal(2, report.TickCount);
            Assert.Equal(2, report.QuoteCount);
            Assert.Equal(2, report.TransportEventCount);
            Assert.Equal(1, report.ActivationCount);
            Assert.Equal(2, report.SymbolsCovered);
            Assert.Equal(1, report.FallbackActivationCount);
            Assert.True(report.TickToQuoteP50Ms > 0);
            Assert.True(report.BroadcastP50Ms > 0);
            Assert.NotNull(report.MaxRecoverySeconds);
            Assert.True(report.MaxRecoverySeconds > 0);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
