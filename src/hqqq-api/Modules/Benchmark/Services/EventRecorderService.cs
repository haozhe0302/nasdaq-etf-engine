using System.Text.Json;
using Microsoft.Extensions.Options;
using Hqqq.Api.Configuration;
using Hqqq.Api.Modules.Benchmark.Contracts;

namespace Hqqq.Api.Modules.Benchmark.Services;

/// <summary>
/// Appends normalized events to a session JSONL file during live runs.
/// Enabled by <c>Recording:Enabled = true</c>. When disabled, all methods
/// are zero-cost no-ops. Writes are serialized by a lock; a periodic timer
/// flushes the OS buffer to disk.
/// </summary>
public sealed class EventRecorderService : IDisposable
{
    private readonly bool _enabled;
    private readonly string _baseDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    private StreamWriter? _writer;
    private readonly object _writerLock = new();
    private Timer? _flushTimer;
    private string? _sessionPath;

    public bool IsEnabled => _enabled;
    public string? SessionPath => _sessionPath;

    public EventRecorderService(IOptions<RecordingOptions> options)
    {
        _enabled = options.Value.Enabled;
        _baseDirectory = options.Value.Directory;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = global::System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

        if (_enabled)
            _flushTimer = new Timer(_ => Flush(), null,
                TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    // ── Public recording methods (no-op when disabled) ───

    public void RecordTick(string symbol, decimal price, string source,
        DateTimeOffset? upstreamTimestamp)
    {
        if (!_enabled) return;
        Write(new RecordedEvent
        {
            EventType = "tick",
            Timestamp = DateTimeOffset.UtcNow,
            Symbol = symbol,
            Price = price,
            Source = source,
            UpstreamTimestamp = upstreamTimestamp,
        });
    }

    public void RecordTransport(string action)
    {
        if (!_enabled) return;
        Write(new RecordedEvent
        {
            EventType = "transport",
            Timestamp = DateTimeOffset.UtcNow,
            Action = action,
        });
    }

    public void RecordQuote(decimal nav, decimal marketPrice,
        decimal premiumDiscountBps, int symbolsTotal, int symbolsStale,
        double broadcastMs, double? tickToQuoteMs)
    {
        if (!_enabled) return;
        Write(new RecordedEvent
        {
            EventType = "quote",
            Timestamp = DateTimeOffset.UtcNow,
            Nav = nav,
            MarketPrice = marketPrice,
            PremiumDiscountBps = premiumDiscountBps,
            SymbolsTotal = symbolsTotal,
            SymbolsStale = symbolsStale,
            BroadcastMs = Math.Round(broadcastMs, 3),
            TickToQuoteMs = tickToQuoteMs is not null
                ? Math.Round(tickToQuoteMs.Value, 3) : null,
        });
    }

    public void RecordActivation(string fingerprint, double jumpBps)
    {
        if (!_enabled) return;
        Write(new RecordedEvent
        {
            EventType = "activation",
            Timestamp = DateTimeOffset.UtcNow,
            Fingerprint = fingerprint,
            JumpBps = Math.Round(jumpBps, 4),
        });
    }

    // ── Internal write path ──────────────────────────────

    private void Write(RecordedEvent evt)
    {
        var line = JsonSerializer.Serialize(evt, _jsonOptions);
        lock (_writerLock)
        {
            EnsureWriter();
            _writer!.WriteLine(line);
        }
    }

    private void EnsureWriter()
    {
        if (_writer is not null) return;

        var date = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var dir = Path.Combine(_baseDirectory, date);
        Directory.CreateDirectory(dir);

        var file = $"session-{DateTime.UtcNow:HHmmss}.jsonl";
        _sessionPath = Path.Combine(dir, file);
        _writer = new StreamWriter(_sessionPath, append: true, encoding: global::System.Text.Encoding.UTF8)
        {
            AutoFlush = false,
        };
    }

    private void Flush()
    {
        lock (_writerLock)
        {
            _writer?.Flush();
        }
    }

    public void Dispose()
    {
        _flushTimer?.Dispose();
        _flushTimer = null;
        lock (_writerLock)
        {
            if (_writer is null) return;
            _writer.Flush();
            _writer.Dispose();
            _writer = null;
        }
    }
}
