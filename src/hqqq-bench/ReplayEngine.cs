using System.Text.Json;
using Hqqq.Api.Modules.Benchmark.Contracts;

namespace Hqqq.Bench;

/// <summary>
/// Reads a JSONL recording file and produces a <see cref="BenchmarkReport"/>
/// by aggregating all recorded events. Works entirely offline — no API keys
/// or live services required.
/// </summary>
public static class ReplayEngine
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static BenchmarkReport Run(
        string inputPath,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null)
    {
        var events = LoadEvents(inputPath, from, to);
        return Aggregate(events);
    }

    public static List<RecordedEvent> LoadEvents(
        string inputPath,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null)
    {
        var files = GetInputFiles(inputPath);
        var events = new List<RecordedEvent>();

        foreach (var file in files)
        {
            foreach (var line in File.ReadLines(file))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var evt = JsonSerializer.Deserialize<RecordedEvent>(line, JsonOpts);
                if (evt is null) continue;
                if (from is not null && evt.Timestamp < from.Value) continue;
                if (to is not null && evt.Timestamp > to.Value) continue;
                events.Add(evt);
            }
        }

        events.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return events;
    }

    public static BenchmarkReport Aggregate(IReadOnlyList<RecordedEvent> events)
    {
        var ticks = events.Where(e => e.EventType == "tick").ToList();
        var quotes = events.Where(e => e.EventType == "quote").ToList();
        var transports = events.Where(e => e.EventType == "transport").ToList();
        var activations = events.Where(e => e.EventType == "activation").ToList();

        var tickToQuoteValues = quotes
            .Where(q => q.TickToQuoteMs is > 0)
            .Select(q => q.TickToQuoteMs!.Value)
            .ToArray();

        var broadcastValues = quotes
            .Where(q => q.BroadcastMs is > 0)
            .Select(q => q.BroadcastMs!.Value)
            .ToArray();

        var recoveryDurations = ComputeRecoveryDurations(transports);
        var fallbackCount = transports.Count(t => t.Action == "fallback_activated");

        var staleRatios = quotes
            .Where(q => q.SymbolsTotal is > 0)
            .Select(q => (double)q.SymbolsStale!.Value / q.SymbolsTotal!.Value)
            .ToArray();

        var basisBpsValues = quotes
            .Where(q => q.Nav is > 0 && q.MarketPrice is > 0)
            .Select(q =>
            {
                var nav = (double)q.Nav!.Value;
                var mkt = (double)q.MarketPrice!.Value;
                return (nav - mkt) / mkt * 10_000;
            })
            .ToArray();

        var symbols = ticks
            .Where(t => t.Symbol is not null)
            .Select(t => t.Symbol!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var sessionStart = events.Count > 0 ? events[0].Timestamp : DateTimeOffset.MinValue;
        var sessionEnd = events.Count > 0 ? events[^1].Timestamp : DateTimeOffset.MinValue;

        return new BenchmarkReport
        {
            SessionStart = sessionStart,
            SessionEnd = sessionEnd,
            Duration = sessionEnd - sessionStart,
            TickCount = ticks.Count,
            QuoteCount = quotes.Count,
            TransportEventCount = transports.Count,
            ActivationCount = activations.Count,
            SymbolsCovered = symbols,

            TickToQuoteP50Ms = Pctl(tickToQuoteValues, 0.50),
            TickToQuoteP95Ms = Pctl(tickToQuoteValues, 0.95),
            TickToQuoteP99Ms = Pctl(tickToQuoteValues, 0.99),
            BroadcastP50Ms = Pctl(broadcastValues, 0.50),
            BroadcastP95Ms = Pctl(broadcastValues, 0.95),

            FallbackActivationCount = fallbackCount,
            RecoveryDurationsSeconds = recoveryDurations,
            MaxRecoverySeconds = recoveryDurations.Length > 0
                ? recoveryDurations.Max() : null,
            P95RecoverySeconds = recoveryDurations.Length >= 2
                ? Pctl(recoveryDurations, 0.95) : recoveryDurations.Length == 1
                ? recoveryDurations[0] : null,

            AvgStaleSymbolRatio = staleRatios.Length > 0
                ? Math.Round(staleRatios.Average(), 4) : 0,
            MaxStaleSymbolRatio = staleRatios.Length > 0
                ? Math.Round(staleRatios.Max(), 4) : 0,
            PctQuotesWithStale = quotes.Count > 0
                ? Math.Round(
                    (double)quotes.Count(q => q.SymbolsStale is > 0) / quotes.Count * 100, 2)
                : 0,

            BasisRmseBps = basisBpsValues.Length > 0
                ? Math.Round(Math.Sqrt(basisBpsValues.Select(b => b * b).Average()), 4) : 0,
            MaxAbsBasisBps = basisBpsValues.Length > 0
                ? Math.Round(basisBpsValues.Select(Math.Abs).Max(), 4) : 0,
            AvgAbsBasisBps = basisBpsValues.Length > 0
                ? Math.Round(basisBpsValues.Select(Math.Abs).Average(), 4) : 0,
        };
    }

    // ── Helpers ──────────────────────────────────────────

    private static double[] ComputeRecoveryDurations(IReadOnlyList<RecordedEvent> transports)
    {
        var durations = new List<double>();
        DateTimeOffset? fallbackStart = null;

        foreach (var t in transports)
        {
            if (t.Action == "fallback_activated")
                fallbackStart = t.Timestamp;
            else if ((t.Action is "ws_recovered" or "fallback_deactivated") && fallbackStart is not null)
            {
                durations.Add((t.Timestamp - fallbackStart.Value).TotalSeconds);
                fallbackStart = null;
            }
        }

        return durations.ToArray();
    }

    public static double Pctl(double[] sorted, double p)
    {
        if (sorted.Length == 0) return 0;
        var copy = (double[])sorted.Clone();
        Array.Sort(copy);
        if (copy.Length == 1) return Math.Round(copy[0], 2);
        var idx = p * (copy.Length - 1);
        var lower = (int)Math.Floor(idx);
        var upper = Math.Min(lower + 1, copy.Length - 1);
        var frac = idx - lower;
        return Math.Round(copy[lower] * (1 - frac) + copy[upper] * frac, 2);
    }

    private static string[] GetInputFiles(string path)
    {
        if (File.Exists(path))
            return [path];

        if (Directory.Exists(path))
            return Directory.GetFiles(path, "*.jsonl", SearchOption.AllDirectories)
                .OrderBy(f => f)
                .ToArray();

        throw new FileNotFoundException($"Recording not found: {path}");
    }
}
