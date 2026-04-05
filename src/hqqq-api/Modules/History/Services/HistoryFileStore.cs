using System.Text.Json;
using Hqqq.Api.Modules.History.Contracts;

namespace Hqqq.Api.Modules.History.Services;

/// <summary>
/// Append-only JSONL store for persisted quote snapshots, date-partitioned
/// under <c>data/history/YYYY-MM-DD/quotes.jsonl</c>.
/// </summary>
public sealed class HistoryFileStore : IDisposable
{
    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _baseDir;
    private readonly ILogger<HistoryFileStore> _logger;

    private StreamWriter? _writer;
    private DateOnly _writerDate;
    private readonly object _writerLock = new();
    private Timer? _flushTimer;

    public HistoryFileStore(string baseDir, ILogger<HistoryFileStore> logger)
    {
        _baseDir = baseDir;
        _logger = logger;
        _flushTimer = new Timer(_ => Flush(), null,
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    // ── Write path ───────────────────────────────────────

    public void Append(HistoryRow row)
    {
        var line = JsonSerializer.Serialize(row, WriteOpts);
        var date = DateOnly.FromDateTime(row.Time.UtcDateTime);
        lock (_writerLock)
        {
            EnsureWriter(date);
            _writer!.WriteLine(line);
        }
    }

    private void EnsureWriter(DateOnly date)
    {
        if (_writer is not null && _writerDate == date) return;

        _writer?.Flush();
        _writer?.Dispose();

        var dir = Path.Combine(_baseDir, date.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "quotes.jsonl");
        _writer = new StreamWriter(path, append: true,
            encoding: global::System.Text.Encoding.UTF8)
        { AutoFlush = false };
        _writerDate = date;
    }

    private void Flush()
    {
        lock (_writerLock) { _writer?.Flush(); }
    }

    // ── Read path ────────────────────────────────────────

    public IReadOnlyList<HistoryRow> LoadRange(DateOnly from, DateOnly to)
    {
        var rows = new List<HistoryRow>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            var path = Path.Combine(_baseDir, d.ToString("yyyy-MM-dd"), "quotes.jsonl");
            if (!File.Exists(path)) continue;

            try
            {
                foreach (var line in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var row = JsonSerializer.Deserialize<HistoryRow>(line, ReadOpts);
                        if (row is not null) rows.Add(row);
                    }
                    catch (JsonException)
                    {
                        // Skip corrupt/partial lines without aborting the rest of the file
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read history file {Path}", path);
            }
        }
        return rows;
    }

    public IReadOnlyList<string> GetAvailableDates()
    {
        if (!Directory.Exists(_baseDir)) return [];
        return Directory.GetDirectories(_baseDir)
            .Select(Path.GetFileName)
            .Where(n => n is not null && DateOnly.TryParse(n, out _))
            .OrderBy(n => n)
            .ToList()!;
    }

    // ── Lifecycle ────────────────────────────────────────

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
